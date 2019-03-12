// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.Mvc.Formatters.Json
{
    internal sealed class TranscodingReadStream : Stream
    {
        internal const int MaxByteBufferSize = 4096;
        internal const int MaxCharBufferSize = 4 * MaxByteBufferSize;
        private static readonly int MaxByteCountForUTF8Char = Encoding.UTF8.GetMaxByteCount(charCount: 1);

        private readonly Stream _stream;
        private readonly Encoder _encoder;
        private readonly Decoder _decoder;

        private ArraySegment<byte> _inputByteBuffer;
        private ArraySegment<char> _charBuffer;

        public TranscodingReadStream(Stream input, Encoding sourceEncoding)
        {
            _stream = input;

            // The "count" in the buffer is the size of any content from a previous read.
            // Initialize them to 0 since nothing has been read so far.
            _inputByteBuffer = new ArraySegment<byte>(
                ArrayPool<byte>.Shared.Rent(MaxByteBufferSize),
                0,
                count: 0);

            // Attempt to allocate a char buffer than can tolerate the worst-case scenario for this 
            // encoding. This would allow the byte -> char conversion to complete in a single call.
            // However limit the buffer size to prevent an encoding that has a very poor worst-case scenario. 
            // The conversion process is tolerant of char buffer that is not large enough to convert all the bytes at once.
            var maxCharBufferSize = Math.Min(MaxCharBufferSize, sourceEncoding.GetMaxCharCount(MaxByteBufferSize));
            _charBuffer = new ArraySegment<char>(
                ArrayPool<char>.Shared.Rent(maxCharBufferSize),
                0,
                count: 0);
            _encoder = Encoding.UTF8.GetEncoder();
            _decoder = sourceEncoding.GetDecoder();
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get; set; }

        public override void Flush()
            => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowArgumentOutOfRangeException(buffer, offset, count);

            var totalBytes = 0;
            int bytesUsed;
            bool encoderCompleted;
            var readBuffer = new ArraySegment<byte>(buffer, offset, count);

            do
            {
                // If we had left-over bytes from a previous read, move it to the start of the buffer and read content in to
                // the segment that follows.
                var eof = false;
                if (_charBuffer.Count == 0)
                {
                    // Only read more content from the input stream if we have exhausted all the buffered chars.
                    eof = await ReadInputChars(cancellationToken);
                }

                // We need to flush on the last write. This is true when we exhaust the input Stream and any buffered content.
                var allContentRead = eof && _charBuffer.Count == 0 && _inputByteBuffer.Count == 0;

                _encoder.Convert(
                    _charBuffer,
                    readBuffer,
                    flush: allContentRead,
                    out var charsUsed,
                    out bytesUsed,
                    out encoderCompleted);

                totalBytes += bytesUsed;
                _charBuffer = _charBuffer.Slice(charsUsed);
                readBuffer = readBuffer.Slice(bytesUsed);

                // We need to exit in one of the 3 conditions:
                // * encoderCompleted will return false if "buffer" was too small for all the chars to be encoded.
                // * no conversion happened in an iteration. This can occur if there wasn't any input.
                // * we do not have enough buffer to accomodate the worst-case scenario of a single char. We may arrive at this case when
                // we were able to accomodate all the chars from previous read (i.e. encoderCompleted -> true), but *may* not have
                // enough to accomodate an additional char in the buffer.
            } while (encoderCompleted && bytesUsed > 0 && readBuffer.Count > MaxByteCountForUTF8Char);

            return totalBytes;
        }

        private async ValueTask<bool> ReadInputChars(CancellationToken cancellationToken)
        {
            // If we had left-over bytes from a previous read, move it to the start of the buffer and read content in to
            // the segment that follows.
            Buffer.BlockCopy(
                _inputByteBuffer.Array,
                _inputByteBuffer.Offset,
                _inputByteBuffer.Array,
                0,
                _inputByteBuffer.Count);

            var readBytes = await _stream.ReadAsync(_inputByteBuffer.Array.AsMemory(_inputByteBuffer.Count), cancellationToken);
            _inputByteBuffer = new ArraySegment<byte>(_inputByteBuffer.Array, 0, _inputByteBuffer.Count + readBytes);

            Debug.Assert(_charBuffer.Count == 0, "We should only expect to read more input chars once all buffered content is read");

            _decoder.Convert(
                _inputByteBuffer.AsSpan(),
                _charBuffer.Array,
                flush: readBytes == 0,
                out var bytesUsed,
                out var charsUsed,
                out _);

            _inputByteBuffer = _inputByteBuffer.Slice(bytesUsed);

            _charBuffer = new ArraySegment<char>(_charBuffer.Array, 0, charsUsed);

            return readBytes == 0;
        }

        private void ThrowArgumentOutOfRangeException(byte[] buffer, int offset, int count)
        {
            if (buffer.Length - offset < count)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (count < MaxByteCountForUTF8Char)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            ArrayPool<char>.Shared.Return(_charBuffer.Array);
            ArrayPool<byte>.Shared.Return(_inputByteBuffer.Array);

            base.Dispose(disposing);
        }
    }
}
