// Unity to ROS frame conversion used by the ImuSensor plugin that adds noise (ImuSensor_88c08315): Unity is left-handed
// with y up and z forward, ROS is right-handed with x forward, y left and z up. Same conversion as ROS#.

using UnityEngine;

public static class Common
{
    public static Vector3 Unity2Ros(this Vector3 vector)
    {
        return new Vector3(vector.z, -vector.x, vector.y);
    }

    public static Quaternion Unity2Ros(this Quaternion quaternion)
    {
        return new Quaternion(-quaternion.z, quaternion.x, -quaternion.y, quaternion.w);
    }
}
