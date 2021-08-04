using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Speech.Synthesis;
using System.Xml.Serialization;
using System.Xml;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace TwitchTTS
{
    class Program
    {

        #region logging
        internal static LogFactory LogFactory;
        private static Logger logger;

        internal static String ApplicationDirectory
        {
            get => Environment.CurrentDirectory;//{ return _ApplicationDirectory ?? (_ApplicationDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location)); }
        }
        private static String _ApplicationDirectory;

        private static Dictionary<string, LogFactory> factories = new Dictionary<string, LogFactory>();

        internal static LogFactory ConfigureLogging(string basename)
        {
            bool bBuiltIn = false;

            string logPath = Path.Combine(ApplicationDirectory, "logs");

            Directory.CreateDirectory(logPath);

            LogFactory factory = null;
            if (!File.Exists(Path.Combine(ApplicationDirectory, "nlog.config")))
            {
                if (factories.ContainsKey(basename))
                    factory = factories[basename];
                else
                {
                    LoggingConfiguration config = new LoggingConfiguration();
                    FileTarget fileTarget = new FileTarget();
                    config.AddTarget(basename, fileTarget);
                    fileTarget.FileName = ApplicationDirectory + "/logs/" + basename + ".log";
                    fileTarget.Encoding = Encoding.UTF8;
                    fileTarget.Layout = "${longdate}|${level:uppercase=true}|${logger}|${message}";// "${longdate}|${level:uppercase=true}|${threadid}|${logger}|${message}";

                    AsyncTargetWrapper asyncFileTarget = new AsyncTargetWrapper(fileTarget, 5000, AsyncTargetWrapperOverflowAction.Discard);
                    config.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, asyncFileTarget));
                    config.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, new ColoredConsoleTarget { Layout = "${longdate}|${level:uppercase=true}|${message}" }));
                    factory = new LogFactory(config);
                    factories[basename] = factory;
                }

                bBuiltIn = true;
            }

            factory.ResumeLogging();

            Logger loggerM = factory.GetLogger("LogService");
            if (loggerM == null)
            {
                Console.WriteLine("logger = null!");
                return null;
            }
            loggerM.Debug("Using {0} configuration", (bBuiltIn ? "Builtin" : "Configfile"));

            return factory;
        }

        static string GetFilename(string fIn)
        {
            return Path.Combine(ApplicationDirectory, fIn);
        }
        #endregion


        #region TTS
        public static List<VoiceInfo> GetInstalledVoices()
        {
            using (var speaker = new SpeechSynthesizer())
            {
                var listOfVoiceInfo = from voice
                                      in speaker.GetInstalledVoices()
                                      select voice.VoiceInfo;

                var tmp = listOfVoiceInfo.ToList<VoiceInfo>();
                logger.Info("Installed Voices:");
                tmp.ForEach(t => logger.Info(t.Name));
                return tmp;
            }
        }

        static VoiceConfig GetVoiceConfig(string speakerVoice)
        {
            var cfg = Config.VoiceConfig.FirstOrDefault(v => v.Matches(speakerVoice));
            if (cfg == null)
            {
                cfg = Config.VoiceConfig.FirstOrDefault(v => v.Matches(DefaultVoice));
            }
            return cfg;
        }

        private static void SpeakText(string text, string speakerVoice = null)
        {
            try
            {
                if (speaker.State == SynthesizerState.Speaking)
                {
                    TextToSpeak.Enqueue(new TextSpeak(text, speakerVoice));
                    return;
                }
                var cfg = GetVoiceConfig(speakerVoice);
                speaker.SelectVoice(cfg.VoiceName);
                speaker.Volume = cfg.Volume;
                logger.Trace("Speak: " + text);
                speaker.SpeakAsync(text);
            }
            catch (ArgumentException aex)
            {
                logger.Warn($"Text2Speech: Cannot set voice to '{speakerVoice}': {aex.Message}");
            }
            catch (Exception ex)
            {
                logger.Warn(ex.ToString());
            }
        }
        #endregion

        #region TwitchAPI
        static bool IsSubscriber(SpeakerConfig who) => true || who.IsSubscriber || who.Name.CompareNoCase(Config.ChannelName);
        static bool IsFollower(SpeakerConfig who) => true;
        static bool IsMod(SpeakerConfig who) => who.IsMod || who.Name.CompareNoCase(Config.ChannelName);
        #endregion

        class TextSpeak
        {
            public string Text;
            public string Voice;

            public TextSpeak(string text, string voice)
            {
                Text = text;
                Voice = voice;
            }
        }

        static Dictionary<string, SpeakerConfig> SpeakerConfigurations = new Dictionary<string, SpeakerConfig>(StringComparer.InvariantCultureIgnoreCase);
        static Dictionary<string, string> TextReplacements = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
        static Dictionary<string, Action<SpeakerConfig, string>> ChatCommands = new Dictionary<string, Action<SpeakerConfig, string>>(StringComparer.InvariantCultureIgnoreCase);
        static ConcurrentQueue<TextSpeak> TextToSpeak = new ConcurrentQueue<TextSpeak>();

        static SpeechSynthesizer speaker;
        static string DefaultVoice = ""; //"IVONA 2 Hans OEM"; //"Vocalizer Expressive Markus Harpo 22kHz";
        static List<VoiceInfo> InstalledVoices;
        static TwitchIRC tts;

        static bool IsTTSEnabled = true;

        static TwitchTTSConfig Config = new TwitchTTSConfig();

        static void Main(string[] args)
        {
           

            LogFactory = ConfigureLogging("twitchtts");
            logger = LogFactory.GetLogger("main");

            InstalledVoices = GetInstalledVoices();
            speaker = new SpeechSynthesizer();
            //speaker.SetOutputToDefaultAudioDevice();
            speaker.Rate = 0;
            speaker.Volume = 100;
            speaker.StateChanged += Speaker_StateChanged;

            LoadConfig();

            if (String.IsNullOrEmpty(Config.DefaultVoice))
                Config.DefaultVoice = InstalledVoices.First().Name;
            DefaultVoice = Config.DefaultVoice;
            InstalledVoices.ForEach(iv =>
            {
                var cfg = Config.VoiceConfig.FirstOrDefault(v => v.Matches(iv.Name));
                if (cfg == null)
                {
                    cfg = new VoiceConfig() { VoiceName = iv.Name, Volume = 100 };
                    Config.VoiceConfig.Add(cfg);
                    SaveConfig();
                }
            });
            SaveConfig();
            ChatCommands["!stimmen"] = (speaker, atext) =>
            {
                StringBuilder sb = new StringBuilder();
                Config.VoiceConfig.ForEach(cfg => sb.Append($"'{cfg.VoiceAlias}',"));
                tts.SendTaggedMsg(speaker.Name, "Verfügbare Stimmen: " + sb);
            };

            ChatCommands["!stimme"] = OnChatCommandSpeakervoice;
            ChatCommands["!name"] = OnChatCommandSpeakername;
            ChatCommands["!block"] = OnChatCommandsModBlock;
            ChatCommands["!unblock"] = OnChatCommandsModUnblock;
            ChatCommands["!toggle"] = OnChatCommandsModToggle;
            ChatCommands["!setname"] = OnChatCommandsModSetName;
            ChatCommands["!setvoice"] = OnChatCommandsModSetVoice;
            ChatCommands["!test"] = OnChatCommandsModTest;
            ChatCommands["!setvolume"] = OnChatCommandsModSetVolume;
            ChatCommands["!default"] = OnChatCommandsModSetDefaultVoice;
            ChatCommands["!alias"] = OnChatCommandsModSetAlias;
            ChatCommands["!replace"] = OnChatCommandsModSetReplace;
            ChatCommands["!prefix"] = OnChatCommandsModSetPrefix;
            //ChatCommands
            ChatCommands["!ttshelp"] = OnChatCommandTTSHelp;

            tts = new TwitchIRC(Config.NickName, Config.ChannelName, Config.OauthToken);
            tts.MessageReceived = OnMessageReceived;
            tts.IsSpeakerBusy = IsSpeakerBusy;
            tts.ConnectionStatusChanged += Tts_ConnectionStatusChanged;
            tts.Start();

            Console.WriteLine("!help for command, 'quit' to exit ...");
            string cCmd;
            while ((cCmd = Console.ReadLine()) != null)
            {
                if (cCmd == "quit")
                    return;
                OnMessageReceived(Config.ChannelName, cCmd, "mod=1", true);
            }

        }

        private static void Tts_ConnectionStatusChanged(object sender, TwitchIRCStatus e)
        {
            logger.Info("Connectionstatus " + e);
            switch (e)
            {
                case TwitchIRCStatus.Offline:
                    break;
                case TwitchIRCStatus.Connecting:
                    break;
                case TwitchIRCStatus.WaitingForAck:
                    break;
                case TwitchIRCStatus.ConnectionFailed:
                    SpeakText("Twitch T T S Keine Verbindung zum Chat!");
                    break;
                case TwitchIRCStatus.Timeout:
                    SpeakText("Twitch T T S Chat Timeout!");
                    break;
                case TwitchIRCStatus.Online:
                    SpeakText("Twitch T T S online. Möge der Saft mit Dir sein!");
                    break;
                default:
                    break;
            }
        }

        private static int GetVoiceVolume(string targetName)
        {
            var t = Config.VoiceConfig.FirstOrDefault(v => v.Matches(targetName));
            return Math.Min(100, (t != null) ? t.Volume : 100);
        }

        private static void OnChatCommandTTSHelp(SpeakerConfig speaker, string args)
        {
            StringBuilder cmds = new StringBuilder();
            cmds.Append("Verfügbare Befehle: !stimmen, !stimme, !name");
            if (IsMod(speaker))
            {
                cmds.Append(" Mod Befehle: !block, !unblock, !toggle, !setname, !setvoice, !test, !setvolume, !default");
            }
            tts.SendTaggedMsg(speaker.Name, cmds.ToString());
        }

        private static void OnChatCommandsModSetVolume(SpeakerConfig speaker, string args)
        {
            if (!IsMod(speaker))
                return;
            var targetVolume = Math.Min(100, Convert.ToInt32(args.GetPart(0, " ")));
            var targetName = args.SkipParts(0, 1, " ");

            var t = Config.VoiceConfig.FirstOrDefault(v => v.Matches(targetName));
            if (t == null)
            {
                Config.VoiceConfig.Add(new VoiceConfig() { VoiceName = targetName, Volume = targetVolume });
            }
            else
            {
                t.Volume = targetVolume;
            }
            SaveConfig();
            tts.SendTaggedMsg(speaker.Name, $"Volume für '{targetName}' auf '{targetVolume}' eingestellt.");

        }

        private static void OnChatCommandsModSetDefaultVoice(SpeakerConfig speaker, string args)
        {
            if (!IsMod(speaker))
                return;
            Config.DefaultVoice = args;
            DefaultVoice = args;
            SaveConfig();
            tts.SendTaggedMsg(speaker.Name, $"Standardstimme  auf '{args}' eingestellt.");
        }

        private static void OnChatCommandsModSetAlias(SpeakerConfig speaker, string args)
        {
            if (!IsMod(speaker))
                return;
            var targetName = args.GetPart(0, "=").Trim();
            var targetNewName = args.SkipParts(0, 1, "=").Trim();


            var t = Config.VoiceConfig.FirstOrDefault(v => v.Matches(targetName));
            if (t == null)
            {
                tts.SendTaggedMsg(speaker.Name, $"Stimme '{targetName}' nicht gefunden.");
            }
            else
            {
                t.VoiceAlias = targetNewName;
            }
            SaveConfig();
            tts.SendTaggedMsg(speaker.Name, $"Alias für '{targetName}' auf '{targetNewName}' eingestellt.");
        }
        private static void OnChatCommandsModSetReplace(SpeakerConfig speaker, string args)
        {
            if (!IsMod(speaker))
                return;
            var targetName = args.GetPart(0, "=").Trim();
            var targetNewName = args.SkipParts(0, 1, "=").Trim();


            var t = Config.VoiceReplaces.FirstOrDefault(vr => String.Compare(vr.Match, targetName, true) == 0);
            if (t == null)
            {
                Config.VoiceReplaces.Add(new VoiceReplace() { Match = targetName, ReplaceWith = targetNewName });
            }
            else
            {
                t.ReplaceWith = targetNewName;
            }
            SaveConfig();
            tts.SendTaggedMsg(speaker.Name, $"Done");
        }
        private static void OnChatCommandsModSetVoice(SpeakerConfig speaker, string args)
        {
            if (!IsMod(speaker))
                return;
            var targetName = args.GetPart(0, " ");
            var targetNewName = args.SkipParts(0, 1, " ");
            var targetConfig = TransformSender(targetName, null);
            targetConfig.Voice = targetNewName;
            SpeakerConfigurations[targetConfig.Name] = targetConfig;
            SaveConfig();
            tts.SendTaggedMsg(speaker.Name, $"Stimme für '{targetName}' auf '{targetNewName}' eingestellt.");
        }

        private static void OnChatCommandsModSetPrefix(SpeakerConfig speaker, string args)
        {
            if (!IsMod(speaker))
                return;
            var targetName = args.GetPart(0, " ");
            var targetNewName = args.SkipParts(0, 1, " ");
            var targetConfig = TransformSender(targetName, null);
            targetConfig.SpokenPrefix = targetNewName;
            SpeakerConfigurations[targetConfig.Name] = targetConfig;
            SaveConfig();
            tts.SendTaggedMsg(speaker.Name, $"Prefix für '{targetName}' auf '{targetNewName}' eingestellt.");
        }

        private static void OnChatCommandsModSetName(SpeakerConfig speaker, string args)
        {
            if (!IsMod(speaker))
                return;
            var targetName = args.GetPart(0, " ");
            var targetNewName = args.SkipParts(0, 1, " ");
            var targetConfig = TransformSender(targetName, null);
            targetConfig.SpokenName = targetNewName;
            SpeakerConfigurations[targetConfig.Name] = targetConfig;
            SaveConfig();
            tts.SendTaggedMsg(speaker.Name, $"Name für '{targetName}' auf '{targetNewName}' eingestellt.");
        }

        private static void OnChatCommandsModTest(SpeakerConfig speaker, string args)
        {
            if (!IsMod(speaker))
                return;
            if (!speaker.IsSubscriber)
                SpeakText("Du bist kein Sub Du Sack!");
            else
                SpeakText("blahfasel");
        }


        private static void OnChatCommandsModToggle(SpeakerConfig speaker, string args)
        {
            if (!IsMod(speaker))
                return;
            IsTTSEnabled = !IsTTSEnabled;
            SpeakText("TTS " + (IsTTSEnabled ? "eingeschaltet" : "ausgeschaltet"));
        }

        private static void OnChatCommandsModBlock(SpeakerConfig speaker, string args)
        {
            if (!IsMod(speaker))
                return;
            var targetConfig = TransformSender(args, null);
            targetConfig.DoSpeak = false;
            SpeakerConfigurations[args] = targetConfig;
            SaveConfig();
            tts.SendTaggedMsg(speaker.Name, $"{args} wird nicht mehr vorgelesen.");
        }

        private static void OnChatCommandsModUnblock(SpeakerConfig speaker, string args)
        {
            if (!IsMod(speaker))
                return;
            var targetConfig = TransformSender(args, null);
            targetConfig.DoSpeak = false;
            SpeakerConfigurations[args] = targetConfig;
            SaveConfig();
            tts.SendTaggedMsg(speaker.Name, $"{args} wird vorgelesen.");
        }

        private static void OnChatCommandSpeakername(SpeakerConfig speaker, string args)
        {
            if (!IsSubscriber(speaker))
                return;
            if (String.IsNullOrEmpty(args))
                speaker.SpokenName = speaker.Name;
            else
                speaker.SpokenName = args;
            SpeakerConfigurations[speaker.Name] = speaker;
            SaveConfig();
            tts.SendTaggedMsg(speaker.Name, $"Name auf '{speaker.SpokenName}' eingestellt.");
        }

        private static void OnChatCommandSpeakervoice(SpeakerConfig speaker, string args)
        {
            if (!IsSubscriber(speaker))
                return;
            var cfg = Config.VoiceConfig.FirstOrDefault(v => v.Matches(args));
            if ((!String.IsNullOrEmpty(args)) && (InstalledVoices.FirstOrDefault(iv => iv.Name.CompareNoCase(cfg.VoiceName)) != null))
            {
                speaker.Voice = args;
                SpeakerConfigurations[speaker.Name] = speaker;
                SaveConfig();
                tts.SendTaggedMsg(speaker.Name, "Stimme eingestellt.");
            }
            else
            {
                tts.SendTaggedMsg(speaker.Name, "Unbekannte Stimme.");
            }


        }

        public static T FromXml<T>(string xml)
        {
            XmlSerializer xsSubmit = new XmlSerializer(typeof(T));

            using (var sww = new StringReader(xml))
            {
                using (XmlReader writer = XmlReader.Create(sww))
                {
                    return (T)xsSubmit.Deserialize(writer);
                }
            }
        }

        public static void SaveObject(object o, string filename)
        {
            XmlSerializer xsSubmit = new XmlSerializer(o.GetType());
            XmlWriterSettings w = new XmlWriterSettings() { Indent = true, Encoding = Encoding.UTF8, OmitXmlDeclaration = true };
            using (var sww = new StringWriter())
            {
                using (XmlWriter writer = XmlWriter.Create(sww, w))
                {
                    xsSubmit.Serialize(writer, o);
                }
                File.WriteAllText(filename, sww.ToString());
            }
        }

        static void LoadConfig()
        {
            if (File.Exists(GetFilename("config/speakers.xml")))
            {
                var tmpSpeakers = FromXml<SpeakerConfigList>(File.ReadAllText(GetFilename("config/speakers.xml")));
                SpeakerConfigurations.Clear();
                tmpSpeakers.speakers.ForEach(s => SpeakerConfigurations[s.Name] = s);
            }
            if (File.Exists(GetFilename("config/config.xml")))
                Config = FromXml<TwitchTTSConfig>(File.ReadAllText(GetFilename("config/config.xml")));

            logger.Info($"Configuration loaded. {SpeakerConfigurations.Count} Speakers");
        }

        static void SaveConfig()
        {
            var tmpSpeakers = new SpeakerConfigList(SpeakerConfigurations.Values.ToList());
            SaveObject(tmpSpeakers, GetFilename("config/speakers.xml"));
            SaveObject(Config, GetFilename("config/config.xml"));
            logger.Info("Configuration saved.");

        }

        private static bool IsSpeakerBusy()
        {
            return (speaker.State == SynthesizerState.Speaking);
        }

        static SpeakerConfig TransformSender(string orgSender, Dictionary<string, string> msgTags)
        {
            SpeakerConfig value = null;
            if (!SpeakerConfigurations.TryGetValue(orgSender, out value))
                value = new SpeakerConfig() { Name = orgSender, SpokenName = orgSender, Voice = DefaultVoice };
            if (msgTags != null)
            {
                if (msgTags.TryGetValue("mod", out string sIsMod))
                    value.IsMod = sIsMod == "1";
                if (msgTags.TryGetValue("subscriber", out string sIsSub))
                    value.IsSubscriber = sIsSub == "1";
            }
            return value;
        }


        private static void Speaker_StateChanged(object sender, StateChangedEventArgs e)
        {
            if (e.State == SynthesizerState.Ready && e.PreviousState == SynthesizerState.Speaking && TextToSpeak.TryDequeue(out var tts))
                SpeakText(tts.Text, tts.Voice);
        }


        static bool OnMessageReceived(string msgSender, string msgText, string msgTagsTxt, bool isConsole = false)
        {
            var allTagTexts = msgTagsTxt.Split(';').ToList();
            var allTags = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            allTagTexts.ForEach(t => allTags[t.GetPart(0, "=")] = t.GetPart(1, "="));

            logger.Info($"{msgSender} ## {msgText}");

            if (String.IsNullOrEmpty(msgText))
                return true;

            var speakerConfig = TransformSender(msgSender, allTags);

            if (msgText[0] == '!')
            {
                var msgCommand = msgText.GetPart(0, " ");
                var msgArgs = msgText.SkipParts(0, 1, " ");
                if (ChatCommands.TryGetValue(msgCommand, out Action<SpeakerConfig, string> act))
                {
                    act(speakerConfig, msgArgs);
                }
                return true;
            }
            if (!IsTTSEnabled)
                return true;

            if (!speakerConfig.DoSpeak)
            {
                logger.Info($"{msgSender} blocked.");
                return true;
            }
            msgText = ParseText(speakerConfig, msgText);

            logger.Debug(msgText);
            SpeakText(msgText, speakerConfig.Voice);
            return true;
        }

        static bool OnMessageReceived(string msg)
        {
            var msgText = msg.GetPart(1, "PRIVMSG #" + Config.ChannelName + " :");
            var msgSender = msg.GetPart(1, " :").GetPart(0, "!");
            var msgTagsTxt = msg.GetPart(0, " :");
            return OnMessageReceived(msgSender, msgText, msgTagsTxt);


        }

        static string lastSpeaker = String.Empty;
        static DateTime lastSpeak = DateTime.MinValue;

        private static string ParseText(SpeakerConfig sender, string text)
        {
            text = StripUTF8(text);
            string prefix = sender.SpokenName + GetVoicePrefix(sender);
            if (text.ToLowerInvariant().Contains("http:") || text.ToLowerInvariant().Contains("https:"))
                text = "Irgendwas mit einem Link drin";
            else
            {
                Config.VoiceReplaces.ForEach(vr => text = Regex.Replace(text, vr.Match, vr.ReplaceWith, RegexOptions.IgnoreCase));
                var sL = SpeakerConfigurations.Values.ToList();
                sL.ForEach(sc => text = Regex.Replace(text, sc.Name, sc.SpokenName, RegexOptions.IgnoreCase));
            }
            if (sender.Name == lastSpeaker && (DateTime.Now - lastSpeak < TimeSpan.FromSeconds(2)))
                prefix = "";
            lastSpeaker = sender.Name;
            lastSpeak = DateTime.Now;
            return prefix + text;
        }

        private static string GetVoicePrefix(SpeakerConfig sender)
        {
            var tmp = GetVoiceConfig(sender?.Voice).VoicePrefix;
            if (!String.IsNullOrEmpty(sender.SpokenPrefix))
                tmp = sender.SpokenPrefix;
            if (String.IsNullOrEmpty(tmp))
                return "";
            return " " + tmp + " ";

        }

        private static string StripUTF8(string inputString)
        {
            return  Encoding.ASCII.GetString(
                Encoding.Convert(
                    Encoding.UTF8,
                    Encoding.GetEncoding(
                        Encoding.ASCII.EncodingName,
                        new EncoderReplacementFallback(string.Empty),
                        new DecoderExceptionFallback()
                        ),
                    Encoding.UTF8.GetBytes(inputString)
                )
            );
        }
    }
}
