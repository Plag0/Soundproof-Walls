
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
        public bool Distortion { get; set; }
        public float PitchMult { get; set; }
        public float MuffleInfluence { get; set; }
        public HashSet<string> KeywordExclusions { get; set; }

        public CustomSound()
        {
            KeywordExclusions = new HashSet<string>();
        }
        public CustomSound(string name, float gainMultiplier, float rangeMultiplier = 1, float sidechainMultiplier = 0, float release = 0, bool distortion = false, float pitchMultiplier = 1, float muffleInfluence = 1, params string[]? exclusions)
        {
            Keyword = name;
            GainMult = gainMultiplier;
            RangeMult = rangeMultiplier;
            SidechainMult = sidechainMultiplier;
            SidechainRelease = release;
            Distortion = distortion;
            PitchMult = pitchMultiplier;
            MuffleInfluence = muffleInfluence;
            KeywordExclusions = new HashSet<string>(exclusions ?? Array.Empty<string>());
        }

        public override bool Equals(object? obj)
        {
            if (obj is not CustomSound other)
            {
                return false;
            }

            return this.Keyword == other.Keyword &&
                   this.GainMult == other.GainMult &&
                   this.RangeMult == other.RangeMult &&
                   this.SidechainMult == other.SidechainMult &&
                   this.SidechainRelease == other.SidechainRelease &&
                   this.Distortion == other.Distortion &&
                   this.PitchMult == other.PitchMult &&
                   this.MuffleInfluence == other.MuffleInfluence &&
                   this.KeywordExclusions.SetEquals(other.KeywordExclusions);
        }

        public override int GetHashCode()
        {
            int hash = 17;
            hash = hash * 31 + (Keyword?.GetHashCode() ?? 0);
            hash = hash * 31 + GainMult.GetHashCode();
            hash = hash * 31 + RangeMult.GetHashCode();
            hash = hash * 31 + SidechainMult.GetHashCode();
            hash = hash * 31 + SidechainRelease.GetHashCode();
            hash = hash * 31 + Distortion.GetHashCode();
            hash = hash * 31 + PitchMult.GetHashCode();
            hash = hash * 31 + MuffleInfluence.GetHashCode();

            // Correctly calculate hash code based on the set's contents
            int exclusionHash = 0;
            foreach (string exclusion in KeywordExclusions.OrderBy(e => e))
            {
                // Use XOR to combine element hashes in an order-independent way
                exclusionHash ^= exclusion.GetHashCode();
            }
            hash = hash * 31 + exclusionHash;

            return hash;
        }
    }
}
