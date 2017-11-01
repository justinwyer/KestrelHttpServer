﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets.Internal
{
    public class SocketSender
    {
        private readonly Socket _socket;
        private readonly SocketAsyncEventArgs _eventArgs = new SocketAsyncEventArgs();
        private readonly SocketAwaitable _awaitable = new SocketAwaitable();

        private List<ArraySegment<byte>> _bufferList;

        public SocketSender(Socket socket)
        {
            _socket = socket;
            _eventArgs.UserToken = this;
            _eventArgs.Completed += (_, e) => ((SocketSender)e.UserToken).SendCompleted(e);
        }

        public SocketAwaitable SendAsync(ReadableBuffer buffers)
        {
            if (buffers.IsSingleSpan)
            {
                return SendAsync(buffers.First);
            }

            _eventArgs.BufferList = GetBufferList(buffers);

            if (!_socket.SendAsync(_eventArgs))
            {
                SendCompleted(_eventArgs);
            }

            return _awaitable;
        }

        private SocketAwaitable SendAsync(Buffer<byte> buffer)
        {
            var segment = GetArraySegment(buffer);

            _eventArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);

            if (!_socket.SendAsync(_eventArgs))
            {
                SendCompleted(_eventArgs);
            }

            return _awaitable;
        }

        private void SendCompleted(SocketAsyncEventArgs e)
        {
            // Clear buffer(s) to prevent the SetBuffer buffer and BufferList from both being
            // set for the next write operation. This is unnecessary for reads since they never
            // set BufferList.

            if (e.BufferList != null)
            {
                e.BufferList.Clear();
                e.BufferList = null;
            }
            else
            {
                e.SetBuffer(null, 0, 0);
            }

            _awaitable.Complete(e.BytesTransferred, e.SocketError);
        }

        private List<ArraySegment<byte>> GetBufferList(ReadableBuffer buffer)
        {
            Debug.Assert(!buffer.IsEmpty);
            Debug.Assert(!buffer.IsSingleSpan);

            if (_bufferList == null)
            {
                _bufferList = new List<ArraySegment<byte>>();
            }

            // We should always clear the list after the send
            Debug.Assert(_bufferList.Count == 0);

            foreach (var b in buffer)
            {
                _bufferList.Add(GetArraySegment(b));
            }

            return _bufferList;
        }

        private static ArraySegment<byte> GetArraySegment(Buffer<byte> buffer)
        {
            if (!buffer.TryGetArray(out var segment))
            {
                throw new InvalidOperationException();
            }

            return segment;
        }
    }
}