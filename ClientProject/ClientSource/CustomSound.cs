
namespace SoundproofWalls
{
    [Serializable]
    public class CustomSound
    {
        public string Keyword { get; set; }
        public float GainMult { get; set; }
        public float RangeMult { get; set; }
        public float SidechainMult { get; set; }
        public float SidechainRelease { get; set; }
        public float PitchMult { get; set; }
        public HashSet<string> KeywordExclusions { get; set; }

        public CustomSound()
        {
            KeywordExclusions = new HashSet<string>();
        }
        public CustomSound(string name, float gainMultiplier, float rangeMultiplier = 1, float sidechainMultiplier = 0, float release = 0, float pitchMultiplier = 1, params string[]? exclusions)
        {
            Keyword = name;
            GainMult = gainMultiplier;
            RangeMult = rangeMultiplier;
            SidechainMult = sidechainMultiplier;
            SidechainRelease = release;
            PitchMult = pitchMultiplier;
            KeywordExclusions = new HashSet<string>(exclusions ?? Array.Empty<string>());
        }
    }

    public class ElementEqualityComparer : IEqualityComparer<CustomSound>
    {
        public bool Equals(CustomSound? x, CustomSound? y)
        {
            if (x == null || y == null) return false;
            
            return x.Keyword == y.Keyword && 
                   x.GainMult == y.GainMult &&
                   x.RangeMult == y.RangeMult &&
                   x.SidechainMult == y.SidechainMult && 
                   x.SidechainRelease == y.SidechainRelease && 
                   x.PitchMult == y.PitchMult &&
                   x.KeywordExclusions.SetEquals(y.KeywordExclusions);
        }

        public int GetHashCode(CustomSound obj)
        {
            if (obj == null) return 0;

            int hash = 17;
            hash = hash * 31 + obj.Keyword.GetHashCode();
            hash = hash * 31 + obj.GainMult.GetHashCode();
            hash = hash * 31 + obj.RangeMult.GetHashCode();
            hash = hash * 31 + obj.SidechainMult.GetHashCode();
            hash = hash * 31 + obj.SidechainRelease.GetHashCode();
            hash = hash * 31 + obj.PitchMult.GetHashCode();
            hash = hash * 31 + obj.KeywordExclusions.GetHashCode();
            return hash;
        }
    }
}
