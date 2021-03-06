﻿using System;
using System.Reflection;
using System.IO;
using System.Text;
using System.Runtime.Serialization;

using FSP = Nessos.FsPickler;

namespace Nessos.CsPickler
{
    /// <summary>
    ///     Provides basic serialization functionality.
    /// </summary>
    public abstract class CsPicklerSerializer
    {
        private FSP.FsPicklerSerializer _pickler;

        /// <summary>
        ///     Wraps an FsPickler instance in a C# friendly facade.
        /// </summary>
        /// <param name="pickler">FsPickler instance.</param>
        public CsPicklerSerializer(FSP.FsPicklerSerializer pickler)
        {
            _pickler = pickler;
        }

        /// <summary>
        ///     Serializes given value to stream.
        /// </summary>
        /// <typeparam name="T">serialized value type.</typeparam>
        /// <param name="stream">target stream.</param>
        /// <param name="value">serialized value.</param>
        /// <param name="streamingContext">payload object for StreamingContext; defaults to null.</param>
        /// <param name="encoding">stream encoding; defaults to UTF8.</param>
        /// <param name="leaveOpen">leave stream open; defaults to false.</param>
        public void Serialize<T>(Stream stream, T value, Object streamingContext = null,
                                    Encoding encoding = null, bool leaveOpen = false)
        {
            var e = Utils.GetEncoding(encoding);
            var sc = Utils.GetStreamingContext(streamingContext);
            var lo = Utils.GetLeaveOpen(leaveOpen);

            _pickler.Serialize<T>(stream, value, streamingContext:sc, encoding:e, leaveOpen:lo);
        }

        /// <summary>
        ///     Deserializes given type from stream.
        /// </summary>
        /// <typeparam name="T">deserialized value type.</typeparam>
        /// <param name="stream">source stream.</param>
        /// <param name="streamingContext">payload object for StreamingContext; defaults to null.</param>
        /// <param name="encoding">stream encoding; defaults to UTF8.</param>
        /// <param name="leaveOpen">leave stream open; defaults to false.</param>
        /// <returns>deserialized value.</returns>
        public T Deserialize<T>(Stream stream, Object streamingContext = null,
                                        Encoding encoding = null, bool leaveOpen = false)
        {
            var e = Utils.GetEncoding(encoding);
            var sc = Utils.GetStreamingContext(streamingContext);
            var lo = Utils.GetLeaveOpen(leaveOpen);

            return _pickler.Deserialize<T>(stream, streamingContext: sc, encoding: e, leaveOpen: lo);
        }

        /// <summary>
        ///     Creates a binary pickle out of a given value.
        /// </summary>
        /// <typeparam name="T">serialized value type.</typeparam>
        /// <param name="value">serialized value.</param>
        /// <param name="streamingContext">payload object for StreamingContext; defaults to null.</param>
        /// <param name="encoding">stream encoding; defaults to UTF8.</param>
        /// <returns>binary pickle for object.</returns>
        public byte [] Pickle<T>(T value, Object streamingContext = null, Encoding encoding = null) 
        {
            var e = Utils.GetEncoding(encoding);
            var sc = Utils.GetStreamingContext(streamingContext);

            return _pickler.Pickle<T>(value, streamingContext: sc, encoding: e);
        }

        /// <summary>
        ///     Instantiates a new object out of a binary pickle.
        /// </summary>
        /// <typeparam name="T">type of value to be unpickled.</typeparam>
        /// <param name="pickle">binary pickle of value.</param>
        /// <param name="streamingContext">payload object for StreamingContext; defaults to null.</param>
        /// <param name="encoding">stream encoding; defaults to UTF8.</param>
        /// <returns>unpickled instance.</returns>
        public T UnPickle<T>(byte[] pickle, Object streamingContext = null, Encoding encoding = null)
        {
            var e = Utils.GetEncoding(encoding);
            var sc = Utils.GetStreamingContext(streamingContext);

            return _pickler.UnPickle<T>(pickle, streamingContext: sc, encoding: e);
        }
    }
}
