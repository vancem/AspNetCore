// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters.Json;

namespace Microsoft.AspNetCore.Mvc.Formatters
{
    /// <summary>
    /// A <see cref="TextOutputFormatter"/> for JSON content that uses <see cref="JsonSerializer"/>.
    /// </summary>
    public class SystemTextJsonOutputFormatter : TextOutputFormatter
    {
        private readonly JsonSerializerOptions _serializerOptions;

        /// <summary>
        /// Initializes a new <see cref="SystemTextJsonOutputFormatter"/> instance.
        /// </summary>
        /// <param name="options">The <see cref="MvcOptions"/>.</param>
        public SystemTextJsonOutputFormatter(MvcOptions options)
        {
            _serializerOptions = options.SerializerOptions;

            SupportedEncodings.Add(Encoding.UTF8);
            SupportedEncodings.Add(Encoding.Unicode);
            SupportedMediaTypes.Add(MediaTypeHeaderValues.ApplicationJson);
            SupportedMediaTypes.Add(MediaTypeHeaderValues.TextJson);
            SupportedMediaTypes.Add(MediaTypeHeaderValues.ApplicationAnyJsonSyntax);
        }

        /// <inheritdoc />
        public sealed override async Task WriteResponseBodyAsync(OutputFormatterWriteContext context, Encoding selectedEncoding)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (selectedEncoding == null)
            {
                throw new ArgumentNullException(nameof(selectedEncoding));
            }

            var httpContext = context.HttpContext;

            var writeStream = GetWriteStream(httpContext, selectedEncoding);
            await JsonSerializer.WriteAsync(context.Object, context.ObjectType, writeStream, _serializerOptions);
            await writeStream.FlushAsync();
        }

        private Stream GetWriteStream(HttpContext httpContext, Encoding selectedEncoding)
        {
            if (selectedEncoding.CodePage == Encoding.UTF8.CodePage)
            {
                // JsonSerializer does not write a BOM. Therefore we do not have to handle it
                // in any special way.
                return httpContext.Response.Body;
            }

            return new TranscodingWriteStream(httpContext.Response.Body, selectedEncoding);
        }
    }
}
