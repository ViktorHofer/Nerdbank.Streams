﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams.Interop.Tests
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using Microsoft;
    using Microsoft.VisualStudio.Threading;
    using Nerdbank.Streams;

    /// <summary>Entrypoint of the test app.</summary>
    internal class Program
    {
        private readonly MultiplexingStream mx;

        private Program(MultiplexingStream mx)
        {
            Requires.NotNull(mx, nameof(mx));
            this.mx = mx;
        }

        private static async Task Main(string[] args)
        {
            ////System.Diagnostics.Debugger.Launch();
            int protocolMajorVersion = int.Parse(args[0]);
            var options = new MultiplexingStream.Options
            {
                TraceSource = { Switch = { Level = SourceLevels.Verbose } },
                ProtocolMajorVersion = protocolMajorVersion,
                DefaultChannelReceivingWindowSize = 64,
                DefaultChannelTraceSourceFactoryWithQualifier = (id, name) => new TraceSource($"Channel {id}") { Switch = { Level = SourceLevels.Verbose } },
            };
            if (protocolMajorVersion >= 3)
            {
                options.SeededChannels.Add(new MultiplexingStream.ChannelOptions());
            }

            MultiplexingStream? mx = await MultiplexingStream.CreateAsync(
                FullDuplexStream.Splice(Console.OpenStandardInput(), Console.OpenStandardOutput()),
                options);
            var program = new Program(mx);
            await program.RunAsync(protocolMajorVersion);
        }

        private static (StreamReader Reader, StreamWriter Writer) CreateStreamIO(MultiplexingStream.Channel channel)
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var reader = new StreamReader(channel.Input.AsStream(), encoding);
            var writer = new StreamWriter(channel.Output.AsStream(), encoding)
            {
                AutoFlush = true,
                NewLine = "\n",
            };
            return (reader, writer);
        }

        private async Task RunAsync(int protocolMajorVersion)
        {
            this.ClientOfferAsync().Forget();
            this.ServerOfferAsync().Forget();

            if (protocolMajorVersion >= 3)
            {
                this.SeededChannelAsync().Forget();
            }

            await this.mx.Completion;
        }

        private async Task ClientOfferAsync()
        {
            MultiplexingStream.Channel? channel = await this.mx.AcceptChannelAsync("clientOffer");
            (StreamReader r, StreamWriter w) = CreateStreamIO(channel);

            // Determine the response to send back based on whether an exception was sent
            string? response;
            if (channel.RemoteException == null)
            {
                string? line = await r.ReadLineAsync();
                response = "recv: " + line;
            }
            else
            {
                response = "Received error: " + channel.RemoteException?.Message;
            }

            await w.WriteLineAsync(response);
        }

        private async Task ServerOfferAsync()
        {
            MultiplexingStream.Channel? channel = await this.mx.OfferChannelAsync("serverOffer");
            (StreamReader r, StreamWriter w) = CreateStreamIO(channel);
            await w.WriteLineAsync("theserver");
            w.Close();
            string? line = await r.ReadLineAsync();
            Assumes.True(line == "recv: theserver");
            r.Close();
        }

        private async Task SeededChannelAsync()
        {
            MultiplexingStream.Channel? channel = this.mx.AcceptChannel(0);
            (StreamReader r, StreamWriter w) = CreateStreamIO(channel);
            string? line = await r.ReadLineAsync();
            await w.WriteLineAsync($"recv: {line}");
        }
    }
}
