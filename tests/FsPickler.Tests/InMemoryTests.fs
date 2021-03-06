﻿namespace Nessos.FsPickler.Tests

open System
open System.IO

open NUnit.Framework

open Nessos.FsPickler

type ``InMemory Tests`` (pickleFormat : string) =
    inherit ``FsPickler Serializer Tests`` (pickleFormat)

    override __.IsRemotedTest = false
    override __.Pickle (value : 'T) = __.PicklerManager.Pickler.Pickle value
    override __.PickleF (pickleF : FsPicklerSerializer -> byte []) = pickleF __.PicklerManager.Pickler