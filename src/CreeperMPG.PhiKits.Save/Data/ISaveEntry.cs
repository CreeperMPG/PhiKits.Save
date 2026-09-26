using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.Data
{
    public interface ISaveEntry
    {
        public string EntryFileName { get; }
        public byte EntryVersion { get; set; }
        public void Deserialize(byte[] data);
        public byte[] Serialize();
        public byte GetEntryVersionBySaveVersion(int saveVersion);
    }
}
