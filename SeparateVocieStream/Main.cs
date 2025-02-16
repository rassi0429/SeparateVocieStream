using Elements.Assets;
using FrooxEngine;
using HarmonyLib;
using ResoniteModLoader;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SeparateVocieStream
{
    public class Main : ResoniteMod
    {
        public override string Name => "SeparateVocieStream";
        public override string Author => "kka429";
        public override string Version => "0.0.1";
        public override string Link => "https://github.com/rassi0429/SeparateVocieStream";


        public override void OnEngineInit()
        {
            // Harmony パッチ
            Harmony harmony = new Harmony("dev.kokoa.SeparateVocieStream");
            harmony.PatchAll();

            FrooxEngine.Engine.Current.OnShutdown += () =>
            {
                Msg("Shutdown");
                Main.SaveWav("output.wav");
            };

        }


        private static List<MonoSample> sampleBuffer = new List<MonoSample>();
        // 前回取得したGlobalIndex
        private static long lastGlobalIndex = 0;

        public static void CollectSamples(OpusStream<MonoSample> opusStream)
        {
            if (opusStream == null || opusStream.Samples == 0)
                return;

            long currentGlobalIndex = opusStream.GlobalIndex;
            
            long newSamplesCount = currentGlobalIndex - lastGlobalIndex;
            if (newSamplesCount <= 0)
                return;

            MonoSample[] samples = new MonoSample[opusStream.Samples];
            opusStream.Read<MonoSample>(samples);

            Main.Msg("GlobalIndex: " + opusStream.GlobalIndex + " newSampleCount: " + newSamplesCount);


            for (int i = 0; i < samples.Length; i++)
            {
                sampleBuffer.Add(samples[i]);
            }

            lastGlobalIndex = lastGlobalIndex + newSamplesCount;
        }

        public static void SaveWav(string filePath, int sampleRate = 44100, short bitsPerSample = 16, short channels = 1)
        {
            int byteRate = sampleRate * channels * bitsPerSample / 8;
            int dataSize = sampleBuffer.Count * channels * (bitsPerSample / 8);
            int riffChunkSize = 36 + dataSize;

            using (var fs = new FileStream(filePath, FileMode.Create))
            using (var bw = new BinaryWriter(fs))
            {
                // RIFFヘッダー
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(riffChunkSize);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));

                // fmtチャンク
                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16); // fmtチャンクのサイズ
                bw.Write((short)1); // PCMフォーマット
                bw.Write(channels);
                bw.Write(sampleRate);
                bw.Write(byteRate);
                bw.Write((short)(channels * bitsPerSample / 8));
                bw.Write(bitsPerSample);

                // dataチャンク
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(dataSize);

                // サンプルデータを書き込む（amplitudeは-1.0～1.0の範囲と仮定）
                foreach (var sample in sampleBuffer)
                {
                    short pcmValue = (short)(sample.amplitude * short.MaxValue);
                    bw.Write(pcmValue);
                }
            }

            // 保存後にバッファをクリアする場合
            sampleBuffer.Clear();
        }




        [HarmonyPatch(typeof(FrooxEngine.Engine), "RunUpdateLoop")]
        class Patch
        {
            static void Postfix(FrooxEngine.Engine __instance)
            {
                FrooxEngine.World w = __instance.WorldManager.FocusedWorld;

                if(w == null)
                {
                    return;
                }

                foreach (FrooxEngine.User user in w.AllUsers)
                {
                    if(user.StreamGroupManager.Groups.Count == 0)
                    {
                        Msg("StreamGroupManager.Groups.Count == 0");
                        continue;
                    }
                    OpusStream<MonoSample> opusStream = null;
                    // OpusStream があるか確認

                    user.Streams.ToList().ForEach(stream =>
                    {
                        if (stream is OpusStream<MonoSample>)
                        {
                            opusStream = stream as OpusStream<MonoSample>;
                        }
                    });

                    if (opusStream == null)
                    {
                        Msg("OpusStream is null");
                        continue;
                    }

                    // opusstreamの音声を取得
                    if(opusStream.Samples == 0)
                    {
                        return;
                    }

                    Main.CollectSamples(opusStream);
                    // MonoSample[] samples = new MonoSample[opusStream.Samples];
                    // opusStream.Read<MonoSample>(samples);
                    //Msg(samples[0].amplitude + " samples.Length: " + samples.Length + ", Last Index: " + opusStream.GlobalIndex);


                }
            }
        }
    }
}
