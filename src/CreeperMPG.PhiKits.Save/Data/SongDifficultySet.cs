using System;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.Data
{
    public class SongDifficultySet<T>
    {
        public T? EZ { get; set; }
        public T? HD { get; set; }
        public T? IN { get; set; }
        public T? AT { get; set; }
        public T? Legacy { get; set; }
        public T? this[int index]
        {
            get
            {
                return index switch
                {
                    0 => EZ,
                    1 => HD,
                    2 => IN,
                    3 => AT,
                    4 => Legacy,
                    _ => throw new IndexOutOfRangeException("Index must be between 0 and 4.")
                };
            }
            set
            {
                switch (index)
                {
                    case 0:
                        EZ = value;
                        break;
                    case 1:
                        HD = value;
                        break;
                    case 2:
                        IN = value;
                        break;
                    case 3:
                        AT = value;
                        break;
                    case 4:
                        Legacy = value;
                        break;
                    default:
                        throw new IndexOutOfRangeException("Index must be between 0 and 4.");
                }
            }
        }
        public Dictionary<string, T?> ToDictionaryWithLegacy()
        {
            return new Dictionary<string, T?>
            {
                ["EZ"] = EZ,
                ["HD"] = HD,
                ["IN"] = IN,
                ["AT"] = AT,
                ["Legacy"] = Legacy,
            };
        }
        public Dictionary<string, T?> ToDictionaryWithoutLegacy()
        {
            return new Dictionary<string, T?>
            {
                ["EZ"] = EZ,
                ["HD"] = HD,
                ["IN"] = IN,
                ["AT"] = AT
            };
        }
        public SongDifficultySet() { }

        public SongDifficultySet(T? ez = default, T? hd = default, T? in_ = default, T? at = default, T? legacy = default)
        {
            EZ = ez; HD = hd; IN = in_; AT = at; Legacy = legacy;
        }

        public SongDifficultySet(IEnumerable<T> infos)
        {
            var e = infos.GetEnumerator();
            EZ = e.MoveNext() ? e.Current : default!;
            HD = e.MoveNext() ? e.Current : default!;
            IN = e.MoveNext() ? e.Current : default!;
            AT = e.MoveNext() ? e.Current : default!;
            Legacy = e.MoveNext() ? e.Current : default!;
        }
    }
}
