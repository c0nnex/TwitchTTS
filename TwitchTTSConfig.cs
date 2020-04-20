using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace TwitchTTS
{
    [XmlRoot(Namespace="http://c0nnex.de/ns")]
    public class TwitchTTSConfig
    {
        public string NickName { get; set; } = "TwitchUsernameDesVorlesers";
        public string ChannelName { get; set; } = "TwitchChannelName";
        public string OauthToken { get; set; } = "OAuthTokesDesVorlesers";
        public string DefaultVoice { get; set; } = "";
        public List<VoiceConfig> VoiceConfig { get; set; } = new List<VoiceConfig>();
        public List<VoiceReplace> VoiceReplaces { get; set; } = new List<VoiceReplace>();
    }

    public class VoiceConfig
    {
        [XmlAttribute] 
        public string VoiceName { get; set; }
        [XmlAttribute] 
        public int Volume { get; set; } = 100;
    }

    public class VoiceReplace
    {
        [XmlAttribute]
        public string Match { get; set; }
        [XmlText]
        public string ReplaceWith { get; set; }

    }
}
