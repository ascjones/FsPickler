﻿namespace Nessos.FsPickler

open System
open System.IO
open System.Reflection
open System.Threading
open System.Runtime.Serialization

open Nessos.FsPickler
open Nessos.FsPickler.Reflection

#if EMIT_IL
open System.Reflection.Emit
open Nessos.FsPickler.Emit
open Nessos.FsPickler.PicklerEmit
#endif

type internal DataContractPickler =

    static member Create<'T>(resolver : IPicklerResolver) =
        let ty = typeof<'T>
        let cacheByRef = not <| ty.IsValueType

        // following specs in http://msdn.microsoft.com/en-us/library/ms733127%28v=vs.110%29.aspx
        let tryGetDataMemberInfo (m : MemberInfo) =
            match tryGetAttr<DataMemberAttribute> m with
            | None -> None
            | Some attr ->
                match m with
                | :? FieldInfo as f -> Some (attr, m, f.FieldType)
                | :? PropertyInfo as p ->
                    if not p.CanRead then
                        let msg = sprintf "property '%s' marked as data member but missing getter." p.Name
                        raise <| new PicklerGenerationException(ty, msg)
                    elif not p.CanWrite then
                        let msg = sprintf "property '%s' marked as data member but missing setter." p.Name
                        raise <| new PicklerGenerationException(ty, msg)

                    Some(attr, m, p.PropertyType)

                | _ -> None

        let dataContractInfo = 
            gatherMembers ty
            |> Seq.choose tryGetDataMemberInfo
            |> Seq.mapi (fun i v -> (i,v))
            // sort data members: primarily specified by user-specified order
            // and secondarily by definition order
            |> Seq.sortBy (fun (i,(attr,_,_)) -> (attr.Order, i))
            |> Seq.map snd
            |> Seq.toArray

        // if type has parameterless constructor, use that on deserialization
        let ctor = ty.GetConstructor(allConstructors, null, [||], [||])
        let members = dataContractInfo |> Array.map (fun (_,m,_) -> m)
        let picklers = dataContractInfo |> Array.map (fun (_,_,t) -> resolver.Resolve t)
        let names = 
            dataContractInfo 
            |> Array.mapi (fun i (attr,m,_) -> 
                match attr.Name with 
                | null | "" -> getNormalizedFieldName i m.Name 
                | name -> getNormalizedFieldName i name)

        let isDeserializationCallback = isAssignableFrom typeof<IDeserializationCallback> typeof<'T>
        let isObjectReference = isAssignableFrom typeof<IObjectReference> typeof<'T>

        let allMethods = typeof<'T>.GetMethods(allMembers)
        let onSerializing = allMethods |> getSerializationMethods<OnSerializingAttribute>
        let onSerialized = allMethods |> getSerializationMethods<OnSerializedAttribute>
        let onDeserializing = allMethods |> getSerializationMethods<OnDeserializingAttribute>
        let onDeserialized = allMethods |> getSerializationMethods<OnDeserializedAttribute>

#if EMIT_IL
        let writerDele =
            DynamicMethod.compileAction3<Pickler [], WriteState, 'T> "dataContractSerializer" (fun picklers writer parent ilGen ->

                emitSerializationMethodCalls onSerializing (Choice1Of4 writer) parent ilGen

                emitSerializeMembers members names writer picklers parent ilGen

                emitSerializationMethodCalls onSerialized (Choice1Of4 writer) parent ilGen

                ilGen.Emit OpCodes.Ret)

        let readerDele =
            DynamicMethod.compileFunc2<Pickler [], ReadState, 'T> "dataContractDeserializer" (fun picklers reader ilGen ->
                // initialize empty value type
                let value = EnvItem<'T>(ilGen)

                // use parameterless constructor, if available
                if ctor = null then
                    emitObjectInitializer typeof<'T> ilGen
                else
                    ilGen.Emit(OpCodes.Newobj, ctor)

                value.Store ()

                emitSerializationMethodCalls onDeserializing (Choice2Of4 reader) value ilGen

                emitDeserializeMembers members names reader picklers value ilGen

                emitSerializationMethodCalls onDeserialized (Choice2Of4 reader) value ilGen

                if isDeserializationCallback then emitDeserializationCallback value ilGen

                if isObjectReference then 
                    emitObjectReferenceResolver<'T, 'T> value (Choice1Of2 reader) ilGen
                else
                    value.Load ()

                ilGen.Emit OpCodes.Ret
            )

        let clonerDele =
            DynamicMethod.compileFunc3<Pickler [], CloneState, 'T, 'T> "dataContractCloner" (fun picklers state value ilGen ->
                // initialize empty value type
                let value' = EnvItem<'T>(ilGen)

                // use parameterless constructor, if available
                if ctor = null then
                    emitObjectInitializer typeof<'T> ilGen
                else
                    ilGen.Emit(OpCodes.Newobj, ctor)

                value'.Store ()

                emitSerializationMethodCalls onSerializing (Choice3Of4 state) value ilGen
                emitSerializationMethodCalls onDeserializing (Choice3Of4 state) value' ilGen

                emitCloneMembers members state picklers value value' ilGen

                emitSerializationMethodCalls onSerialized (Choice3Of4 state) value ilGen
                emitSerializationMethodCalls onDeserialized (Choice3Of4 state) value' ilGen

                if isDeserializationCallback then emitDeserializationCallback value' ilGen

                if isObjectReference then 
                    emitObjectReferenceResolver<'T, 'T> value' (Choice2Of2 state) ilGen
                else
                    value'.Load ()

                ilGen.Emit OpCodes.Ret
            )

        let accepter =
            if members.Length = 0 then ignore2
            else
                let accepterDele =
                    DynamicMethod.compileAction3<Pickler [], VisitState, 'T> "dataContractAccepter" (fun picklers state value ilGen ->

                        emitSerializationMethodCalls onSerializing (Choice4Of4 state) value ilGen

                        emitAcceptMembers members state picklers value ilGen

                        emitSerializationMethodCalls onSerialized (Choice4Of4 state) value ilGen

                        ilGen.Emit OpCodes.Ret)

                fun v t -> accepterDele.Invoke(picklers, v, t)

        let writer w t v = writerDele.Invoke(picklers, w, v)
        let reader r t = readerDele.Invoke(picklers, r)
        let cloner s t = clonerDele.Invoke(picklers, s, t)
                
#else
        let inline run (ms : MethodInfo []) (x : obj) w =
            for i = 0 to ms.Length - 1 do 
                ms.[i].Invoke(x, [| getStreamingContext w :> obj |]) |> ignore

        let writer (w : WriteState) (tag : string) (t : 'T) =
            run onSerializing t w

            for i = 0 to members.Length - 1 do
                let value =
                    match members.[i] with
                    | :? PropertyInfo as p -> p.GetValue t
                    | :? FieldInfo as f -> f.GetValue t
                    | _ -> invalidOp "internal error on serializing '%O'." typeof<'T>

                picklers.[i].UntypedWrite w names.[i] value

            run onSerialized t w

        let reader (r : ReadState) (tag : string) =
            let t =
                // use parameterless constructor, if available
                if obj.ReferenceEquals(ctor, null) then
                    FormatterServices.GetUninitializedObject(ty) |> fastUnbox<'T>
                else
                    ctor.Invoke(null) |> fastUnbox<'T>

            run onDeserializing t r

            for i = 0 to members.Length - 1 do
                let value = picklers.[i].UntypedRead r names.[i]
                match members.[i] with
                | :? PropertyInfo as p -> p.SetValue(t, value)
                | :? FieldInfo as f -> f.SetValue(t, value)
                | _ -> invalidOp <| sprintf "internal error on deserializing '%O'." typeof<'T>

            run onDeserialized t r
            if isDeserializationCallback then (fastUnbox<IDeserializationCallback> t).OnDeserialization null
            if isObjectReference then 
                (fastUnbox<IObjectReference> t).GetRealObject r.StreamingContext :?> 'T
            else
                t

        let cloner (c : CloneState) (t : 'T) =
            let t' =
                // use parameterless constructor, if available
                if obj.ReferenceEquals(ctor, null) then
                    FormatterServices.GetUninitializedObject(ty) |> fastUnbox<'T>
                else
                    ctor.Invoke(null) |> fastUnbox<'T>

            run onSerializing t c
            run onDeserializing t' c

            for i = 0 to members.Length - 1 do
                match members.[i] with
                | :? PropertyInfo as p -> 
                    let o = p.GetValue t
                    let o' = picklers.[i].UntypedClone c o
                    p.SetValue(t', o')

                | :? FieldInfo as f -> 
                    let o = f.GetValue t
                    let o' = picklers.[i].UntypedClone c o
                    f.SetValue(t', o')

                | _ -> invalidOp "internal error on cloning '%O'." typeof<'T>

            run onSerialized t c
            run onDeserialized t' c
            if isDeserializationCallback then (fastUnbox<IDeserializationCallback> t').OnDeserialization null
            if isObjectReference then 
                (fastUnbox<IObjectReference> t').GetRealObject c.StreamingContext :?> 'T
            else
                t'

        let accepter (v : VisitState) (t : 'T) =
            run onSerializing t v

            for i = 0 to members.Length - 1 do
                let value =
                    match members.[i] with
                    | :? PropertyInfo as p -> p.GetValue t
                    | :? FieldInfo as f -> f.GetValue t
                    | _ -> invalidOp "internal error on visiting '%O'." typeof<'T>

                picklers.[i].UntypedAccept v value

            run onSerialized t v

#endif

        CompositePickler.Create(reader, writer, cloner, accepter, PicklerInfo.DataContract, cacheByRef = cacheByRef, useWithSubtypes = false)