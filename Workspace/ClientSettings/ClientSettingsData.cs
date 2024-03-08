﻿using Galaxy_Swapper_v2.Workspace.Swapping.Other;
using Galaxy_Swapper_v2.Workspace.Utilities;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;

namespace Galaxy_Swapper_v2.Workspace.ClientSettings
{
    public class ClientSettingsData
    {
        public byte[] Buffer = null!;

        public string AccessToken = null!;
        public string AccountID = null!;

        public uint Magic;
        public int Engine;

        public bool Deserialize()
        {
            Log.Information("Deserializing ClientSettings buffer");

            try
            {
                var reader = new Reader(Buffer);

                Magic = reader.Read<uint>();

                if (!Magic.Equals(0x44464345)) //ClientSettings.sav is not compressed
                {
                    reader.Position = 0;
                    Buffer = reader.ReadBytes((int)(reader.BaseStream.Length));
                    return true;
                }

                Engine = reader.Read<int>();

                var isCompressed = reader.ReadBoolean(true);

                Log.Information("ClientSettings is compressed: {0}", isCompressed);

                if (isCompressed)
                {
                    var decompressedSize = reader.Read<int>();
                    var compressedSize = (int)(reader.BaseStream.Length - reader.BaseStream.Position);
                    byte[] compressedBuffer = reader.ReadBytes(compressedSize);

                    Log.Information("Decompressing ClientSettings buffer");

                    Buffer = Decompress(compressedBuffer, decompressedSize);
                }
                else
                {
                    Buffer = reader.ReadBytes((int)(reader.BaseStream.Length - reader.Position));
                }

                //I will write the full deserilize when I'm back from vacation but due to me being on a time contrant it will be left as this.

                return true;
            }
            catch (Exception exception)
            {
                Log.Error(exception.Message);
                Message.DisplaySTA("Error", $"Failed to deserialize clientsettings.sav buffer!\n\n{exception.Message}", System.Windows.MessageBoxButton.OK, discord: true);
                return false;
            }
        }

        public bool Serialize(out byte[] serialized)
        {
            //I will write the full Serialize when I'm back from vacation but due to me being on a time contrant it will be left as this.

            serialized = Buffer;
            return false;
        }

        public bool ModifyFov(float fov)
        {
            byte[] minFov = new byte[] { 0x46, 0x4F, 0x56, 0x4D, 0x69, 0x6E, 0x69, 0x6D, 0x75, 0x6D, 0x00, 0x0E, 0x00, 0x00, 0x00, 0x46, 0x6C, 0x6F, 0x61, 0x74, 0x50, 0x72, 0x6F, 0x70, 0x65, 0x72, 0x74, 0x79, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            byte[] maxFov = new byte[] { 0x46, 0x4F, 0x56, 0x4D, 0x61, 0x78, 0x69, 0x6D, 0x75, 0x6D, 0x00, 0x0E, 0x00, 0x00, 0x00, 0x46, 0x6C, 0x6F, 0x61, 0x74, 0x50, 0x72, 0x6F, 0x70, 0x65, 0x72, 0x74, 0x79, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

            Log.Information("Modifying fov values");

            try
            {
                int minFovPos = Buffer.IndexOfSequence(minFov, 0);
                int maxFovPos = Buffer.IndexOfSequence(maxFov, 0);

                if (minFovPos < 0 || maxFovPos < 0)
                {
                    throw new Exception("Failed to find fov position in clientsettings.sav buffer");
                }
                
                Log.Information("minFovPos: {0}, maxFovPos: {1}", minFovPos, maxFovPos);

                minFovPos += minFov.Length;
                maxFovPos += maxFov.Length;

                var writer = new Writer(Buffer);

                writer.Position = minFovPos;
                writer.Write<float>(fov - 1);

                writer.Position = maxFovPos;
                writer.Write<float>(fov);

                Buffer = writer.ToByteArray(writer.BaseStream.Length);

                return true;
            }
            catch (Exception exception)
            {
                Log.Error(exception.Message);
                Message.DisplaySTA("Error", $"Failed to modify FOV values!\n\n{exception.Message}", System.Windows.MessageBoxButton.OK, discord: true);
                return false;
            }
        }

        public bool Authenticate(string authorization_code)
        {
            var stopwatch = new Stopwatch(); stopwatch.Start();
            const string url = "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/token";

            using (var client = new RestClient())
            {
                var request = new RestRequest(new Uri(url), Method.Post);
                request.AddHeader("Authorization", "Basic MzQ0NmNkNzI2OTRjNGE0NDg1ZDgxYjc3YWRiYjIxNDE6OTIwOWQ0YTVlMjVhNDU3ZmI5YjA3NDg5ZDMxM2I0MWE=");

                request.AddParameter("grant_type", "authorization_code");
                request.AddParameter("token_type", "eg1");
                request.AddParameter("code", authorization_code);

                Log.Information($"Sending {request.Method} request to {url}");

                var response = client.Execute(request);

                if (response is null)
                {
                    Message.DisplaySTA("Error", "Failed to execute authenticate to epic games server.", discord: true, solutions: new[] { "Disable Windows Defender Firewall", "Disable any anti-virus softwares" });
                    return false;
                }

                if (response.StatusCode != System.Net.HttpStatusCode.OK || response.Content is null || !response.Content.ValidJson())
                {
                    Message.DisplaySTA("Error", "Authorization code is invalid. Please make sure you copied and\npasted the code correctly and that it has not expired!", discord: true);
                    return false;
                }

                var parse = JsonConvert.DeserializeObject<JObject>(response.Content);

                AccessToken = parse["access_token"].Value<string>();
                AccountID = parse["account_id"].Value<string>();

                Log.Information($"Finished {request.Method} request in {stopwatch.GetElaspedAndStop().ToString("mm':'ss")} received {response.Content.Length}");

                return true;
            }
        }

        public bool Download()
        {
            var stopwatch = new Stopwatch(); stopwatch.Start();
            string url = $"https://fngw-mcp-gc-livefn.ol.epicgames.com/fortnite/api/cloudstorage/user/{AccountID}/ClientSettings.Sav";

            using (var client = new RestClient())
            {
                var request = new RestRequest(new Uri(url), Method.Get);
                request.AddHeader("Authorization", $"Bearer {AccessToken}");

                Log.Information($"Sending {request.Method} request to {url}");

                var response = client.Execute(request);

                if (response is null)
                {
                    Message.DisplaySTA("Error", "Failed to download 'ClientSettings.Sav' file from epic games server!", discord: true, solutions: new[] { "Disable Windows Defender Firewall", "Disable any anti-virus softwares" });
                    return false;
                }

                if (response.StatusCode != System.Net.HttpStatusCode.OK || response.RawBytes is null)
                {
                    Message.DisplaySTA("Error", "Authorization code is expired. Please make sure you copied and\npasted the code correctly and that it has not expired!", discord: true);
                    return false;
                }

                Buffer = response.RawBytes;

                Log.Information($"Finished {request.Method} request in {stopwatch.GetElaspedAndStop().ToString("mm':'ss")} received {response.Content.Length}");

                return true;
            }
        }

        public bool Upload(byte[] clientSettingsBuffer)
        {
            var stopwatch = new Stopwatch(); stopwatch.Start();
            string url = $"https://fngw-mcp-gc-livefn.ol.epicgames.com/fortnite/api/cloudstorage/user/{AccountID}/ClientSettings.Sav";

            using (var client = new RestClient())
            {
                var request = new RestRequest(new Uri(url), Method.Put);
                request.AddHeader("Authorization", $"Bearer {AccessToken}");
                request.AddParameter("application/octet-stream", clientSettingsBuffer, ParameterType.RequestBody);

                Log.Information($"Sending {request.Method} request to {url}");

                var response = client.Execute(request);

                if (response is null)
                {
                    Message.DisplaySTA("Error", "Failed to upload 'ClientSettings.Sav' file to epic games server!", discord: true, solutions: new[] { "Disable Windows Defender Firewall", "Disable any anti-virus softwares" });
                    return false;
                }

                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    Message.DisplaySTA("Error", "Authorization code is expired. Please make sure you copied and\npasted the code correctly and that it has not expired!", discord: true);
                    return false;
                }

                Log.Information($"Finished {request.Method} request in {stopwatch.GetElaspedAndStop().ToString("mm':'ss")} received {response.Content.Length}");

                return true;
            }
        }

        #region Compression
        private static byte[] Compress(byte[] inputData)
        {
            using (var compressedStream = new MemoryStream())
            {
                using (var deflaterStream = new DeflaterOutputStream(compressedStream))
                {
                    deflaterStream.Write(inputData, 0, inputData.Length);
                }

                return compressedStream.ToArray();
            }
        }
        private static byte[] Decompress(byte[] compressedData, int decompressedSize)
        {
            using (var decompressedStream = new MemoryStream(decompressedSize))
            {
                using (var compressedStream = new MemoryStream(compressedData))
                {
                    using (var inflaterStream = new InflaterInputStream(compressedStream))
                    {
                        inflaterStream.CopyTo(decompressedStream);
                        decompressedStream.Seek(0, SeekOrigin.Begin);

                        return decompressedStream.ToArray();
                    }
                }
            }
        }
        #endregion
    }
}