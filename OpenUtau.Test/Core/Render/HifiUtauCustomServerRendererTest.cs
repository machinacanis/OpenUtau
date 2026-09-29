using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.CustomRender;
using OpenUtau.Core.Format;
using OpenUtau.Core.HiFiUtau;
using OpenUtau.Core.HiFiUtau.Engine.Pipeline;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.Core.Render {
    [CollectionDefinition("HifiUtauSerial", DisableParallelization = true)]
    public class HifiUtauSerialCollection { }

    /// <summary>
    /// HiFiUTAU / Custom Server 新增面的可用性测试。
    /// 只覆盖产品契约：渲染器接线、oudep 布局、音素 JSON、mel 拼接位置。
    /// 不覆盖 ONNX 推理、HTTP 往返、STFT/mel 数值金标准。
    /// </summary>
    public class HifiUtauCustomServerRendererTest {
        [Collection("HifiUtauSerial")]
        public class Renderer {
            [Fact]
            public void HifiUtau_IsInProcessClassicRenderer() {
                var renderer = Renderers.CreateRenderer(Renderers.HIFIUTAU);
                Assert.IsType<HifiUtauRenderer>(renderer);
                Assert.Equal(Renderers.HIFIUTAU, renderer!.ToString());
                Assert.Equal(USingerType.Classic, renderer.SingerType);
                Assert.DoesNotContain("HTTP", renderer.GetType().FullName);
            }

            [Fact]
            public void HifiUtau_MigratesLegacyRendererNames() {
                Assert.IsType<HifiUtauRenderer>(Renderers.CreateRenderer("HIFIUTAU_LOCAL"));
                Assert.IsType<HifiUtauRenderer>(Renderers.CreateRenderer("HIFIUTAU_ONLINE"));
            }

            [Fact]
            public void CustomServer_StaysIndependentOfHifiUtau() {
                var renderer = Renderers.CreateRenderer(Renderers.CUSTOM_SERVER);
                Assert.IsType<CustomServerRenderer>(renderer);
                Assert.Equal(Renderers.CUSTOM_SERVER, renderer!.ToString());
                Assert.Equal(USingerType.Classic, renderer.SingerType);
                Assert.IsNotType<HifiUtauRenderer>(renderer);
            }

            [Fact]
            public void ClassicSinger_OffersBothNewRenderersWithoutDroppingWorldline() {
                var supported = Renderers.GetSupportedRenderers(USingerType.Classic);
                Assert.Equal(
                    new[] { Renderers.WORLDLINE_R, Renderers.WORLDLINE_R11, Renderers.CLASSIC, Renderers.HIFIUTAU, Renderers.CUSTOM_SERVER },
                    supported);
            }

            [Fact]
            public void TrackSettings_MigratesLegacyHifiNameAndBindsCustomServerUrl() {
                var project = new UProject();
                var track = project.tracks[0];
                track.Singer = new FoundClassicSinger();

                track.RendererSettings.renderer = "HIFIUTAU_LOCAL";
                track.RendererSettings.Validate(track);
                Assert.Equal(Renderers.HIFIUTAU, track.RendererSettings.renderer);
                Assert.IsType<HifiUtauRenderer>(track.RendererSettings.Renderer);

                track.RendererSettings.renderer = Renderers.CUSTOM_SERVER;
                track.RendererSettings.serverUrl = "http://127.0.0.1:9000";
                track.RendererSettings.endpoint = "/synthesize";
                track.RendererSettings.Validate(track);
                var custom = Assert.IsType<CustomServerRenderer>(track.RendererSettings.Renderer);
                Assert.Equal("http://127.0.0.1:9000", custom.ServerUrl);
                Assert.Equal("/synthesize", custom.Endpoint);
            }

            [Fact]
            public void TrackSettings_PersistsRendererAndServerUrlInYaml() {
                var settings = new URenderSettings {
                    renderer = Renderers.CUSTOM_SERVER,
                    serverUrl = "http://127.0.0.1:9000",
                    endpoint = "/synthesize",
                };
                var loaded = Yaml.DefaultDeserializer.Deserialize<URenderSettings>(
                    Yaml.DefaultSerializer.Serialize(settings));
                Assert.Equal(Renderers.CUSTOM_SERVER, loaded.renderer);
                Assert.Equal("http://127.0.0.1:9000", loaded.serverUrl);
                Assert.Equal("/synthesize", loaded.endpoint);
            }

            [Fact]
            public void CustomServer_SplitsFullUrlIntoHostAndEndpoint() {
                var renderer = new CustomServerRenderer("http://127.0.0.1:9000/synthesize");
                Assert.Equal("http://127.0.0.1:9000", renderer.ServerUrl);
                Assert.Equal("/synthesize", renderer.Endpoint);
            }
        }

        public class Oudep {
            [Fact]
            public void VocoderOudep_RequiresYamlAndNamedOnnx() {
                using var dir = new TempDir();
                File.WriteAllText(Path.Combine(dir.Path, "vocoder.yaml"),
                    "name: pc_nsf_hifigan_44.1k_hop512_128bin_2025.02\nmodel: model.onnx\n");
                File.WriteAllBytes(Path.Combine(dir.Path, "model.onnx"), new byte[] { 1 });
                Assert.True(HiFiUtauModelStore.IsValidVocoderOudepDir(dir.Path));
            }

            [Fact]
            public void VocoderOudep_RejectsMissingNamedOnnx() {
                using var dir = new TempDir();
                File.WriteAllText(Path.Combine(dir.Path, "vocoder.yaml"),
                    "name: pc_nsf_hifigan_44.1k_hop512_128bin_2025.02\nmodel: missing.onnx\n");
                File.WriteAllBytes(Path.Combine(dir.Path, "model.onnx"), new byte[] { 1 });
                Assert.False(HiFiUtauModelStore.IsValidVocoderOudepDir(dir.Path));
            }

            [Fact]
            public void VocoderOudep_RejectsPytorchCkptDirectory() {
                using var dir = new TempDir();
                File.WriteAllText(Path.Combine(dir.Path, "config.json"), "{}");
                File.WriteAllBytes(Path.Combine(dir.Path, "model.ckpt"), new byte[] { 1 });
                Assert.True(HiFiUtauModelStore.LooksLikePytorchCkptDir(dir.Path));
                Assert.False(HiFiUtauModelStore.IsValidVocoderOudepDir(dir.Path));
            }

            [Fact]
            public void VocoderOudepId_MatchesInstalledPackageName() {
                Assert.Equal("pc_nsf_hifigan_44.1k_hop512_128bin_2025.02",
                    HiFiUtauModelStore.PcNsfHifiGanOudepId);
            }

            [Fact]
            public void HnsepOudep_RequiresOudepYamlAndOnnx() {
                using var dir = new TempDir();
                File.WriteAllText(Path.Combine(dir.Path, "oudep.yaml"),
                    "id: hnsep_VR_44.1k_hop512_2024.05\nclass: Hnsep\n");
                File.WriteAllBytes(Path.Combine(dir.Path, "hnsep.onnx"), new byte[] { 1 });
                Assert.True(HiFiUtauModelStore.IsValidHnsepOudepDir(dir.Path));
            }

            [Fact]
            public void HnsepOudep_RejectsRawOnnxDirectory() {
                using var dir = new TempDir();
                File.WriteAllText(Path.Combine(dir.Path, "config.yaml"), "sr: 44100\n");
                File.WriteAllBytes(Path.Combine(dir.Path, "hnsep.onnx"), new byte[] { 1 });
                Assert.False(HiFiUtauModelStore.IsValidHnsepOudepDir(dir.Path));
            }

            [Fact]
            public void HnsepOudepId_MatchesPackedPackageName() {
                Assert.Equal("hnsep_VR_44.1k_hop512_2024.05", HiFiUtauModelStore.HnsepOudepId);
            }
        }

        [Collection("HifiUtauSerial")]
        public class Expressions {
            [Fact]
            public void RegistersHifiExpressions_WithoutUtauFlagOnStretchMs() {
                var project = new UProject();
                Ustx.AddDefaultExpressions(project);

                Assert.Equal(UExpressionType.Curve, project.expressions[Ustx.GWL].type);
                Assert.Equal(UExpressionType.Curve, project.expressions[Ustx.LOWC].type);
                Assert.Equal(UExpressionType.Curve, project.expressions[Ustx.WARM].type);
                Assert.Equal(UExpressionType.Options, project.expressions[Ustx.PHTP].type);
                Assert.Equal(UExpressionType.Options, project.expressions[Ustx.STRT].type);
                Assert.Equal(UExpressionType.Options, project.expressions[Ustx.SPLC].type);
                Assert.True(string.IsNullOrEmpty(project.expressions[Ustx.STMS].flag));
            }

            [Fact]
            public void StretchMs_DoesNotLeakIntoResamplerFlags() {
                var phrase = PhraseFactory.Create(
                    configure: (project, track, phoneme) => {
                        phoneme.SetExpression(project, track, Ustx.STMS, 12);
                        phoneme.SetExpression(project, track, Ustx.GEN, 30);
                    });
                var flags = phrase.Phoneme.GetResamplerFlags(phrase.Project, phrase.Track);
                Assert.DoesNotContain(flags, f => f.Item3 == Ustx.STMS);
                Assert.DoesNotContain(flags, f => f.Item1 == "S");
                Assert.Contains(flags, f => f.Item3 == Ustx.GEN && f.Item1 == "g");
            }
        }

        [Collection("HifiUtauSerial")]
        public class PhraseJson {
            [Fact]
            public void RenderPhone_CarriesHifiNoteOptions() {
                var phrase = PhraseFactory.Create(
                    configure: (project, track, phoneme) => {
                        phoneme.SetExpression(project, track, Ustx.PHTP, 2);
                        phoneme.SetExpression(project, track, Ustx.STRT, 1);
                        phoneme.SetExpression(project, track, Ustx.SPLC, 1);
                        phoneme.SetExpression(project, track, Ustx.STMS, 12);
                    });
                var phone = Assert.Single(phrase.Render.phones);
                Assert.Equal(2, phone.phonemeType);
                Assert.Equal(1, phone.stretchMode);
                Assert.Equal(1, phone.spliceMode);
                Assert.Equal(12, phone.stretchMs);
            }

            [Fact]
            public void HifiPhraseJson_IsParseableByInProcessEngine() {
                var phrase = PhraseFactory.Create(
                    configure: (project, track, phoneme) => {
                        phoneme.SetExpression(project, track, Ustx.STMS, 12);
                        phoneme.SetExpression(project, track, Ustx.SPLC, 1);
                    });
                var data = PhraseData.Parse(HifiUtauPhraseJson.Build(phrase.Render));
                Assert.Equal(256, data.HopSize);
                var phone = Assert.Single(data.Phones);
                Assert.Equal("a", phone.Name);
                Assert.Equal(12, phone.Flag("stms", -1));
                Assert.Equal(1, phone.Flag("splc", -1));
                Assert.True(phone.Flags.ContainsKey("vel"));
            }

            [Fact]
            public void CustomPhraseJson_IsIndependentHttpPayload() {
                var phrase = PhraseFactory.Create(
                    renderer: Renderers.CUSTOM_SERVER,
                    configure: (project, track, phoneme) => {
                        phoneme.SetExpression(project, track, Ustx.STMS, 12);
                    });
                var json = CustomPhraseJson.Build(phrase.Render);
                Assert.Equal(256, json.Value<int>("hop_size"));
                Assert.Equal(44100, json.Value<int>("sample_rate"));
                Assert.NotNull(json["phoneme_list"]);
                Assert.NotNull(json["Dynamic_parameter"]);
                Assert.Equal("a", json["phoneme_list"]["1"]["phoneme_name"].ToString());
                Assert.Equal(12, (double)json["phoneme_list"]["1"]["Note_flags"]["stms"]);
            }

            [Fact]
            public void DefaultBrightness_IsSampledIntoPhraseAndJson() {
                var hifi = PhraseFactory.Create();
                Assert.NotNull(hifi.Render.warmth);
                Assert.NotEmpty(hifi.Render.warmth);
                var hifiJson = HifiUtauPhraseJson.Build(hifi.Render);
                var hifiWarm = hifiJson["Dynamic_parameter"]["warm"];
                Assert.NotNull(hifiWarm);
                Assert.True(hifiWarm.HasValues);

                var custom = PhraseFactory.Create(renderer: Renderers.CUSTOM_SERVER);
                Assert.NotNull(custom.Render.warmth);
                var customJson = CustomPhraseJson.Build(custom.Render);
                var customWarm = customJson["Dynamic_parameter"]["warm"];
                Assert.NotNull(customWarm);
                Assert.True(customWarm.HasValues);
            }
        }

        public class AudioResample {
            [Fact]
            public void Resample_AdvancesThroughSourceInsteadOfRepeatingTheStart() {
                const int fromSr = 22050;
                const int toSr = 44100;
                var input = new float[fromSr];
                int half = input.Length / 2;
                for (int i = half; i < input.Length; i++) {
                    input[i] = 1f;
                }

                var output = AudioReader.Resample(input, fromSr, toSr);
                Assert.True(output.Length > input.Length);

                int quarter = output.Length / 4;
                float first = MeanAbs(output, 0, quarter);
                float last = MeanAbs(output, output.Length - quarter, quarter);
                Assert.True(first < 0.1f, $"first quarter should be near 0, got {first}");
                Assert.True(last > 0.9f, $"last quarter should be near 1, got {last}");
            }

            static float MeanAbs(float[] samples, int start, int length) {
                double sum = 0;
                for (int i = 0; i < length; i++) {
                    sum += Math.Abs(samples[start + i]);
                }
                return (float)(sum / length);
            }
        }

        public class MelSplice {
            [Fact]
            public void PositionsOverlapThePreviousPhoneOnTheTimeline() {
                var first = Phone(p0: 0, p1: 5, p4: 100);
                var second = Phone(p0: 0, p1: 10, p4: 80);
                var (starts, preutters, _, _, vfs, ends) = FragmentMel.CalcPositionsAndRatiosMs(
                    new List<PhoneInfo> { first, second });

                double spm = 44100 / 1000.0;
                Assert.Equal(0, starts[0]);
                Assert.Equal(90 * spm, starts[1], 6);
                Assert.Equal(170 * spm, ends[1], 6);
                Assert.Equal(1, vfs[0]);
                Assert.Equal(90 * spm, preutters[1], 6);
            }

            static PhoneInfo Phone(double p0, double p1, double p4) {
                var info = new PhoneInfo {
                    P0 = new EnvPoint { X = p0, Y = 0 },
                    P1 = new EnvPoint { X = p1, Y = 100 },
                    P4 = new EnvPoint { X = p4, Y = 0 },
                    Oto = new OtoEntry { Consonant = 20, Preutter = 10 },
                };
                info.Flags["vel"] = 100;
                return info;
            }
        }

        sealed class FoundClassicSinger : USinger {
            readonly UOto oto = new UOto();

            public FoundClassicSinger() {
                found = true;
                loaded = true;
            }

            public override string Id => "hifi-test-classic";
            public override string Name => "hifi-test-classic";
            public override USingerType SingerType => USingerType.Classic;
            public override IList<USubbank> Subbanks => Array.Empty<USubbank>();
            public override bool TryGetOto(string phoneme, out UOto oto) {
                oto = this.oto;
                return true;
            }
        }

        sealed class PhraseFactory {
            public UProject Project { get; init; } = null!;
            public UTrack Track { get; init; } = null!;
            public UPhoneme Phoneme { get; init; } = null!;
            public RenderPhrase Render { get; init; } = null!;

            public static PhraseFactory Create(
                string renderer = Renderers.HIFIUTAU,
                Action<UProject, UTrack, UPhoneme> configure = null) {
                var project = new UProject();
                Ustx.AddDefaultExpressions(project);
                var track = project.tracks[0];
                track.Singer = new FoundClassicSinger();
                track.RendererSettings.renderer = renderer;
                if (renderer == Renderers.CUSTOM_SERVER) {
                    track.RendererSettings.serverUrl = "http://127.0.0.1:8000";
                    track.RendererSettings.endpoint = "/synthesize";
                }
                track.RendererSettings.Validate(track);

                var note = UNote.Create();
                note.lyric = "a";
                note.tone = 60;
                note.position = 0;
                note.duration = 480;
                note.ExtendedDuration = 480;
                note.pitch.AddPoint(new PitchPoint(-5, 0));
                note.pitch.AddPoint(new PitchPoint(5, 0));

                var part = new UVoicePart {
                    trackNo = 0,
                    position = 0,
                    duration = 960,
                };
                part.notes.Add(note);

                var phoneme = new UPhoneme {
                    Parent = note,
                    phoneme = "a",
                    position = 0,
                };
                configure?.Invoke(project, track, phoneme);
                note.Validate(new ValidateOptions(), project, track, part);
                phoneme.Validate(new ValidateOptions(), project, track, part, note);
                Assert.False(phoneme.Error, phoneme.ErrorException?.ToString() ?? "phoneme.Error");
                part.phonemes.Add(phoneme);

                var phrases = RenderPhrase.FromPart(project, track, part);
                return new PhraseFactory {
                    Project = project,
                    Track = track,
                    Phoneme = phoneme,
                    Render = Assert.Single(phrases),
                };
            }
        }

        sealed class TempDir : IDisposable {
            public string Path { get; } = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "ou-hifi-" + Guid.NewGuid().ToString("N"));

            public TempDir() => Directory.CreateDirectory(Path);

            public void Dispose() {
                try {
                    if (Directory.Exists(Path)) {
                        Directory.Delete(Path, true);
                    }
                } catch {
                }
            }
        }
    }
}
