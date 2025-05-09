
namespace SoundproofWalls
{
    [Serializable]
    public class CustomSound
    {
        public string Name { get; set; }
        public float GainMultiplier { get; set; }
        public float RolloffFactor { get; set; }
        public float SidechainMultiplier { get; set; }
        public float Release { get; set; }
        public HashSet<string> Exclusions { get; set; }

        public CustomSound()
        {
            Exclusions = new HashSet<string>();
        }
        public CustomSound(string name, float gainMultiplier, float rolloffFactor = 1, float sidechainMultiplier = 0, float release = 0, params string[]? exclusions)
        {
            Name = name;
            GainMultiplier = gainMultiplier;
            RolloffFactor = rolloffFactor;
            SidechainMultiplier = sidechainMultiplier;
            Release = release;
            Exclusions = new HashSet<string>(exclusions ?? Array.Empty<string>());
        }
    }

    public class ElementEqualityComparer : IEqualityComparer<CustomSound>
    {
        public bool Equals(CustomSound? x, CustomSound? y)
        {
            if (x == null || y == null) return false;
            
            return x.Name == y.Name && 
                   x.GainMultiplier == y.GainMultiplier &&
                   x.RolloffFactor == y.RolloffFactor &&
                   x.SidechainMultiplier == y.SidechainMultiplier && 
                   x.Release == y.Release && 
                   x.Exclusions.SetEquals(y.Exclusions);
        }

        public int GetHashCode(CustomSound obj)
        {
            if (obj == null) return 0;

            int hash = 17;
            hash = hash * 31 + obj.Name.GetHashCode();
            hash = hash * 31 + obj.GainMultiplier.GetHashCode();
            hash = hash * 31 + obj.RolloffFactor.GetHashCode();
            hash = hash * 31 + obj.SidechainMultiplier.GetHashCode();
            hash = hash * 31 + obj.Release.GetHashCode();
            hash = hash * 31 + obj.Exclusions.GetHashCode();
            return hash;
        }
    }
}
