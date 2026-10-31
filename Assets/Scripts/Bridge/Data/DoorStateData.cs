// Door lock state and window opening (0, 25, 50, 75 or 100 percent) of four doors, received by the door state sensor.
// Only the plugin using it remains, so the fields are rebuilt from the members it uses.

namespace Simulator.Bridge.Data
{
    public class DoorStateData
    {
        public byte Door1_State;
        public byte Door2_State;
        public byte Door3_State;
        public byte Door4_State;

        public uint Door1_Percent;
        public uint Door2_Percent;
        public uint Door3_Percent;
        public uint Door4_Percent;
    }
}
