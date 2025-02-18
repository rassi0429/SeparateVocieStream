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
    class InjectClass : IAudioUpdatable
    {
        World w;

        public InjectClass(World w)
        {
            this.w = w;
        }

        public void InternalRunAudioConfigurationChanged()
        {
            // do nothing
            // throw new System.NotImplementedException();
        }

        public void InternalRunAudioUpdate()
        {
            foreach (FrooxEngine.User user in w.AllUsers)
            {
                if (user.StreamGroupManager.Groups.Count == 0)
                {
                    Main.Msg("StreamGroupManager.Groups.Count == 0");
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
                    Main.Msg("OpusStream is null");
                    continue;
                }

                // opusstreamの音声を取得
                if (opusStream.Samples == 0)
                {
                    return;
                }

                Main.CollectSamples(opusStream);
            }
        }
    }

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

            FrooxEngine.Engine.Current.OnReady += () =>
            {
                Msg("Ready");

            };
            Engine.Current.RunPostInit(() =>
            {
                Msg("PostInit");

                Engine.Current.WorldManager.WorldFocused += (FrooxEngine.World world) =>
                {
                    Msg("WorldFocused: " + world.Name);

                    if(world.Name == "Local")
                    {
                        world.UpdateManager.RegisterForAudioUpdates((IAudioUpdatable)new InjectClass(world));
                        return;
                    }
                };
            });

        }


        private static List<float> sampleBuffer = new List<float>();
        // 前回取得したGlobalIndex
        private static long lastGlobalIndex = 0;

        public static void CollectSamples(OpusStream<MonoSample> opusStream)
        {
            if (opusStream == null || opusStream.Samples == 0)
                return;

            long currentGlobalIndex = opusStream.GlobalIndex;

            if (currentGlobalIndex <= lastGlobalIndex)
                return;

            long newSamplesCount = 1024;
            if (newSamplesCount <= 0)
                return;

            float[] samples = new float[newSamplesCount];
            opusStream.Read<MonoSample>(samples.AsMonoBuffer());

            Main.Msg("GlobalIndex: " + opusStream.GlobalIndex + " newSampleCount: " + newSamplesCount);


            for (int i = 0; i < samples.Length ; i++)
            {
                sampleBuffer.Add(samples[i]);
            }

            lastGlobalIndex = currentGlobalIndex;
        }

        public static void SaveWav(string filePath, int sampleRate = 44100, short bitsPerSample = 16, short channels = 1)
        {
            int byteRate = sampleRate * channels * bitsPerSample / 8;
            int dataSize = sampleBuffer.Count * channels * (bitsPerSample / 8);
            int riffChunkSize = 36 + dataSize;


            using (var fs = new FileStream(filePath + ".txt", FileMode.Create))
            using (var sw = new StreamWriter(fs))
            {
                sw.WriteLine("sampleBuffer.Count: " + sampleBuffer.Count);
                sw.WriteLine("riffChunkSize: " + riffChunkSize);
                sw.WriteLine("dataSize: " + dataSize);
                for (int i = 0; i < sampleBuffer.Count;
                    i++)
                {
                    sw.WriteLine(sampleBuffer[i]);
                }

                sw.Flush();
                sw.Close();
            }

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
                    short pcmValue = (short)(sample * short.MaxValue);
                    bw.Write(pcmValue);
                }
            }

            // 保存後にバッファをクリアする場合
            sampleBuffer.Clear();
        }
    }
}
