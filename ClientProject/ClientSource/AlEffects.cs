
using Barotrauma;
using OpenAL;
using System.Runtime.InteropServices;

namespace SoundproofWalls
{
    public static class AlEffects
    {
        // Effect Slot Properties
        public const int AL_EFFECTSLOT_EFFECT = 0x0001;
        public const int AL_EFFECTSLOT_GAIN = 0x0002;
        public const int AL_EFFECTSLOT_AUXILIARY_SEND_AUTO = 0x0003;
        public const int AL_EFFECTSLOT_NULL = 0x0000; // Used to detach effects/sends

        // Effect Types
        public const int AL_EFFECT_NULL = 0x0000;
        public const int AL_EFFECT_REVERB = 0x0001;
        public const int AL_EFFECT_CHORUS = 0x0002;
        public const int AL_EFFECT_DISTORTION = 0x0003;
        public const int AL_EFFECT_ECHO = 0x0004;
        public const int AL_EFFECT_FLANGER = 0x0005;
        public const int AL_EFFECT_FREQUENCY_SHIFTER = 0x0006;
        public const int AL_EFFECT_VOCAL_MORPHER = 0x0007;
        public const int AL_EFFECT_PITCH_SHIFTER = 0x0008;
        public const int AL_EFFECT_RING_MODULATOR = 0x0009;
        public const int AL_EFFECT_AUTOWAH = 0x000A;
        public const int AL_EFFECT_COMPRESSOR = 0x000B;
        public const int AL_EFFECT_EQUALIZER = 0x000C;
        public const int AL_EFFECT_EAXREVERB = 0x8000;

        // Effect Properties
        public const int AL_EFFECT_TYPE = 0x8001; 

        // Reverb Parameters (AL_EFFECT_REVERB)
        public const int AL_REVERB_DENSITY = 0x0001;
        public const int AL_REVERB_DIFFUSION = 0x0002;
        public const int AL_REVERB_GAIN = 0x0003;
        public const int AL_REVERB_GAINHF = 0x0004;
        public const int AL_REVERB_DECAY_TIME = 0x0005;
        public const int AL_REVERB_DECAY_HFRATIO = 0x0006;
        public const int AL_REVERB_REFLECTIONS_GAIN = 0x0007;
        public const int AL_REVERB_REFLECTIONS_DELAY = 0x0008;
        public const int AL_REVERB_LATE_REVERB_GAIN = 0x0009;
        public const int AL_REVERB_LATE_REVERB_DELAY = 0x000A;
        public const int AL_REVERB_AIR_ABSORPTION_GAINHF = 0x000B;
        public const int AL_REVERB_ROOM_ROLLOFF_FACTOR = 0x000C;
        public const int AL_REVERB_DECAY_HFLIMIT = 0x000D; // Boolean (True/False mapped to 1/0)

        // Distortion Effect Parameters (AL_EFFECT_DISTORTION)
        public const int AL_DISTORTION_EDGE = 0x0001;
        public const int AL_DISTORTION_GAIN = 0x0002;
        public const int AL_DISTORTION_LOWPASS_CUTOFF = 0x0003;
        public const int AL_DISTORTION_EQCENTER = 0x0004;
        public const int AL_DISTORTION_EQBANDWIDTH = 0x0005;

        // Equalizer Parameters (for AL_EFFECT_EQUALIZER)
        public const int AL_EQUALIZER_LOW_GAIN = 0x0001; // float, 0.126 to 7.943 (~-18dB to +18dB), def 1.0
        public const int AL_EQUALIZER_LOW_CUTOFF = 0x0002; // float, 50.0 to 800.0 Hz, def 200.0
        public const int AL_EQUALIZER_MID1_GAIN = 0x0003; // float, 0.126 to 7.943, def 1.0
        public const int AL_EQUALIZER_MID1_CENTER = 0x0004; // float, 200.0 to 3000.0 Hz, def 500.0
        public const int AL_EQUALIZER_MID1_WIDTH = 0x0005; // float, 0.01 to 1.0 (Q factor related), def 1.0
        public const int AL_EQUALIZER_MID2_GAIN = 0x0006; // float, 0.126 to 7.943, def 1.0
        public const int AL_EQUALIZER_MID2_CENTER = 0x0007; // float, 1000.0 to 8000.0 Hz, def 3000.0
        public const int AL_EQUALIZER_MID2_WIDTH = 0x0008; // float, 0.01 to 1.0, def 1.0
        public const int AL_EQUALIZER_HIGH_GAIN = 0x0009; // float, 0.126 to 7.943, def 1.0
        public const int AL_EQUALIZER_HIGH_CUTOFF = 0x000A; // float, 4000.0 to 16000.0 Hz, def 6000.0


        // Filter Types
        public const int AL_FILTER_NULL = 0x0000;
        public const int AL_FILTER_LOWPASS = 0x0001;
        public const int AL_FILTER_HIGHPASS = 0x0002;
        public const int AL_FILTER_BANDPASS = 0x0003;

        // Filter Properties
        public const int AL_FILTER_TYPE = 0x8001;

        // Lowpass Parameters (AL_FILTER_LOWPASS)
        public const int AL_LOWPASS_GAIN = 0x0001;
        public const int AL_LOWPASS_GAINHF = 0x0002;

        // Bandpass Parameters (AL_FILTER_BANDPASS)
        public const int AL_BANDPASS_GAIN = 0x0001;
        public const int AL_BANDPASS_GAINLF = 0x0002;
        public const int AL_BANDPASS_GAINHF = 0x0003;

        // Source Properties for EFX
        public const int AL_DIRECT_FILTER = 0x20005;
        public const int AL_AUXILIARY_SEND_FILTER = 0x20006;
        public const int AL_AIR_ABSORPTION_FACTOR = 0x20007;
        public const int AL_ROOM_ROLLOFF_FACTOR = 0x20008; // Note: Source specific Room Rolloff
        public const int AL_CONE_OUTER_GAINHF = 0x20009;
        public const int AL_DIRECT_FILTER_GAINHF_AUTO = 0x2000A; // Boolean
        public const int AL_AUXILIARY_SEND_FILTER_GAIN_AUTO = 0x2000B; // Boolean
        public const int AL_AUXILIARY_SEND_FILTER_GAINHF_AUTO = 0x2000C; // Boolean

        // Context Properties for EFX
        public const int ALC_EFX_MAJOR_VERSION = 0x20001;
        public const int ALC_EFX_MINOR_VERSION = 0x20002;
        public const int ALC_MAX_AUXILIARY_SENDS = 0x20003;

        // --- Delegate Definitions ---
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlGenAuxiliaryEffectSlots(int n, uint[] slots);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlDeleteAuxiliaryEffectSlots(int n, uint[] slots);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool AlIsAuxiliaryEffectSlot(uint slot);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlAuxiliaryEffectSloti(uint slot, int param, int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlAuxiliaryEffectSlotf(uint slot, int param, float value);


        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlGenEffects(int n, uint[] effects);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlDeleteEffects(int n, uint[] effects);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool AlIsEffect(uint effect);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlEffecti(uint effect, int param, int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlEffectf(uint effect, int param, float value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlEffectfv(uint effect, int param, float[] values);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlGetEffecti(uint effect, int param, out int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlGetFilteri(uint effect, int param, out int value);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlGenFilters(int n, uint[] filters);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlDeleteFilters(int n, uint[] filters);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool AlIsFilter(uint filter);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlFilteri(uint filter, int param, int value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlFilterf(uint filter, int param, float value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlFilterfv(uint filter, int param, float[] values);

        // For AL_AUXILIARY_SEND_FILTER - parameters are uint effectSlotId, int sendIndex, uint filterId
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlSource3i_SendFilter(uint source, int param, uint value1, int value2, uint value3);
        // For AL_DIRECT_FILTER - parameter is uint filterId
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlSourcei_Filter(uint source, int param, uint value);
        // For Source EFX floats (AL_AIR_ABSORPTION_FACTOR etc.)
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlSourcef_efx(uint source, int param, float value);
        // For Source EFX booleans (xxx_AUTO)
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AlSourcei_efx_bool(uint source, int param, int value); // Use int 0/1 for bool


        // --- Loaded Function Pointers ---
        private static AlGenAuxiliaryEffectSlots? _alGenAuxiliaryEffectSlots;
        private static AlDeleteAuxiliaryEffectSlots? _alDeleteAuxiliaryEffectSlots;
        private static AlIsAuxiliaryEffectSlot? _alIsAuxiliaryEffectSlot;
        private static AlAuxiliaryEffectSloti? _alAuxiliaryEffectSloti;
        private static AlAuxiliaryEffectSlotf? _alAuxiliaryEffectSlotf; // For setting floats

        private static AlGenEffects? _alGenEffects;
        private static AlDeleteEffects? _alDeleteEffects;
        private static AlIsEffect? _alIsEffect;
        private static AlEffecti? _alEffecti;
        private static AlEffectf? _alEffectf;
        private static AlEffectfv? _alEffectfv;
        private static AlGetEffecti? _alGetEffecti;
        private static AlGetFilteri? _alGetFilteri;

        private static AlGenFilters? _alGenFilters;
        private static AlDeleteFilters? _alDeleteFilters;
        private static AlIsFilter? _alIsFilter;
        private static AlFilteri? _alFilteri;
        private static AlFilterf? _alFilterf;
        private static AlFilterfv? _alFilterfv;


        // --- Public Accessor Methods ---
        public static void GenAuxiliaryEffectSlots(int n, uint[] slots) => _alGenAuxiliaryEffectSlots?.Invoke(n, slots);
        public static void DeleteAuxiliaryEffectSlots(int n, uint[] slots) => _alDeleteAuxiliaryEffectSlots?.Invoke(n, slots);
        public static bool IsAuxiliaryEffectSlot(uint slot) => _alIsAuxiliaryEffectSlot?.Invoke(slot) ?? false;
        public static void AuxiliaryEffectSloti(uint slot, int param, uint effectIdValue) { _alAuxiliaryEffectSloti?.Invoke(slot, param, (int)effectIdValue); }
        public static void AuxiliaryEffectSloti(uint slot, int param, int intValue) { _alAuxiliaryEffectSloti?.Invoke(slot, param, intValue); }
        public static void AuxiliaryEffectSlotf(uint slot, int param, float value) => _alAuxiliaryEffectSlotf?.Invoke(slot, param, value); // Use this for AL_EFFECTSLOT_GAIN

        public static void GenEffects(int n, uint[] effects) => _alGenEffects?.Invoke(n, effects);
        public static void DeleteEffects(int n, uint[] effects) => _alDeleteEffects?.Invoke(n, effects);
        public static bool IsEffect(uint effect) => _alIsEffect?.Invoke(effect) ?? false;
        public static void Effecti(uint effect, int param, int value) => _alEffecti?.Invoke(effect, param, value);
        public static void Effectf(uint effect, int param, float value) => _alEffectf?.Invoke(effect, param, value);
        public static void Effectfv(uint effect, int param, float[] values) => _alEffectfv?.Invoke(effect, param, values);
        public static void GetEffecti(uint effect, int param, out int value)
        {
            value = 0; // Default assignment for the out parameter
            _alGetEffecti?.Invoke(effect, param, out value);
        }
        public static void GetFilteri(uint effect, int param, out int value)
        {
            value = 0; // Default assignment for the out parameter
            _alGetFilteri?.Invoke(effect, param, out value);
        }

        public static void GenFilters(int n, uint[] filters) => _alGenFilters?.Invoke(n, filters);
        public static void DeleteFilters(int n, uint[] filters) => _alDeleteFilters?.Invoke(n, filters);
        public static bool IsFilter(uint filter) => _alIsFilter?.Invoke(filter) ?? false;
        public static void Filteri(uint filter, int param, int value) => _alFilteri?.Invoke(filter, param, value);
        public static void Filterf(uint filter, int param, float value) => _alFilterf?.Invoke(filter, param, value);
        public static void Filterfv(uint filter, int param, float[] values) => _alFilterfv?.Invoke(filter, param, values);

        // --- Initialization ---
        private static bool _efxInitialized = false;
        public static bool IsInitialized => _efxInitialized;
        public static int MaxAuxiliarySends { get; private set; } = 0;

        // Helper function to load delegates
        private static T? LoadDelegate<T>(IntPtr device, string name) where T : Delegate
        {
            IntPtr ptr = Alc.GetProcAddress(device, name);
            if (ptr == IntPtr.Zero)
            {
                DebugConsole.NewMessage($"[SoundproofWalls][AlEffects] Failed to load EFX function pointer: {name}");
                // Consider throwing an exception or handling the error appropriately depending on function importance
                return null;
            }
            try
            {
                return Marshal.GetDelegateForFunctionPointer<T>(ptr);
            }
            catch (Exception ex)
            {
                DebugConsole.NewMessage($"[SoundproofWalls][AlEffects] Failed to get delegate for {name}: {ex.Message}");
                return null;
            }
        }


        public static bool Initialize(IntPtr device)
        {
            if (_efxInitialized) return true;

            // Check if the extension is present on the device
            if (!Alc.IsExtensionPresent(device, "ALC_EXT_EFX"))
            {
                DebugConsole.NewMessage("[SoundproofWalls][AlEffects] ALC_EXT_EFX not supported.");
                return false;
            }

            // Get Max Auxiliary Sends
            Alc.GetInteger(device, ALC_MAX_AUXILIARY_SENDS, out int maxSends);
            if (Alc.GetError(device) != Alc.NoError)
            {
                DebugConsole.NewMessage("[SoundproofWalls][AlEffects] Could not query ALC_MAX_AUXILIARY_SENDS.");
                // Proceeding, but MaxAuxiliarySends might be incorrect (0).
                maxSends = 0; // Default to 0 if query fails
            }
            MaxAuxiliarySends = maxSends;

            // Load function pointers using Alc.GetProcAddress

            _alGenAuxiliaryEffectSlots = LoadDelegate<AlGenAuxiliaryEffectSlots>(device, "alGenAuxiliaryEffectSlots");
            _alDeleteAuxiliaryEffectSlots = LoadDelegate<AlDeleteAuxiliaryEffectSlots>(device, "alDeleteAuxiliaryEffectSlots");
            _alIsAuxiliaryEffectSlot = LoadDelegate<AlIsAuxiliaryEffectSlot>(device, "alIsAuxiliaryEffectSlot");
            _alAuxiliaryEffectSloti = LoadDelegate<AlAuxiliaryEffectSloti>(device, "alAuxiliaryEffectSloti");
            _alAuxiliaryEffectSlotf = LoadDelegate<AlAuxiliaryEffectSlotf>(device, "alAuxiliaryEffectSlotf");

            _alGenEffects = LoadDelegate<AlGenEffects>(device, "alGenEffects");
            _alDeleteEffects = LoadDelegate<AlDeleteEffects>(device, "alDeleteEffects");
            _alIsEffect = LoadDelegate<AlIsEffect>(device, "alIsEffect");
            _alEffecti = LoadDelegate<AlEffecti>(device, "alEffecti");
            _alEffectf = LoadDelegate<AlEffectf>(device, "alEffectf");
            _alEffectfv = LoadDelegate<AlEffectfv>(device, "alEffectfv");
            _alGetEffecti = LoadDelegate<AlGetEffecti>(device, "alGetEffecti");
            _alGetFilteri = LoadDelegate<AlGetFilteri>(device, "alGetFilteri");

            _alGenFilters = LoadDelegate<AlGenFilters>(device, "alGenFilters");
            _alDeleteFilters = LoadDelegate<AlDeleteFilters>(device, "alDeleteFilters");
            _alIsFilter = LoadDelegate<AlIsFilter>(device, "alIsFilter");
            _alFilteri = LoadDelegate<AlFilteri>(device, "alFilteri");
            _alFilterf = LoadDelegate<AlFilterf>(device, "alFilterf");
            _alFilterfv = LoadDelegate<AlFilterfv>(device, "alFilterfv");

            // Check if all essential delegates were loaded
            _efxInitialized = _alGenAuxiliaryEffectSlots != null &&
                             _alDeleteAuxiliaryEffectSlots != null &&
                             _alIsAuxiliaryEffectSlot != null &&
                             _alAuxiliaryEffectSloti != null &&
                             _alAuxiliaryEffectSlotf != null &&
                             _alGenEffects != null &&
                             _alDeleteEffects != null &&
                             _alIsEffect != null &&
                             _alEffecti != null &&
                             _alEffectf != null &&
                             _alEffectfv != null &&
                             _alGetEffecti != null &&
                             _alGetFilteri != null &&
                             _alGenFilters != null &&
                             _alDeleteFilters != null &&
                             _alIsFilter != null &&
                             _alFilteri != null &&
                             _alFilterf != null &&
                             _alFilterfv != null;


            if (_efxInitialized)
            {
                DebugConsole.NewMessage("[SoundproofWalls] OpenAL Effects Extension loaded successfully.");
            }
            else
            {
                DebugConsole.NewMessage("[SoundproofWalls] OpenAL Effects Extension failed to load: One or more function pointers could not be loaded.");
            }


            return _efxInitialized;
        }

        public static void Cleanup()
        {
            _efxInitialized = false;
            MaxAuxiliarySends = 0;
        }
    }
}
