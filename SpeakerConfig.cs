using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace TwitchTTS
{
    [Serializable]
    public class SpeakerConfig
    {
        [XmlAttribute]
        public string Name { get; set; }

        [XmlAttribute]
        public string SpokenName { get; set; }

        [XmlAttribute]
        public string SpokenPrefix { get; set; } = null;

        [XmlAttribute]
        public string Voice { get; set; }

        [XmlAttribute]
        public bool CanCustomize { get; set; } = true;

        [XmlAttribute]
        public bool DoSpeak { get; set; } = true;

        [XmlIgnore]
        public bool IsMod = false;
        [XmlIgnore]
        public bool IsSubscriber = false;


    }

    [XmlRoot(Namespace = "http://c0nnex.de/ns")]
   public class SpeakerConfigList
    {
        [XmlArrayItem(ElementName ="Speaker")]
        public List<SpeakerConfig> speakers = new List<SpeakerConfig>();
        
        public SpeakerConfigList() { }
        public SpeakerConfigList(List<SpeakerConfig> list)
        {
            this.speakers = list;
        }
    }
}
