// IMU noise model parameters used by the ImuSensor plugin that adds noise (ImuSensor_88c08315). Its noise model is
// AirSim's ImuSimple; the original values are lost with the assembly that held them, so these are AirSim's defaults
// (ImuSimpleParams, Bosch BMI085).

using UnityEngine;

namespace Simulator.Sensors
{
    public static class SensorParams
    {
        const float Gravity = 9.80665f;

        // Gyroscope: angular random walk of 0.30 deg/sqrt(hour) in rad/sqrt(s), bias stability of 4.6 deg/hour in
        // rad/s, with a 500 s correlation time
        public static float gyro_arw = 0.30f / Mathf.Sqrt(3600.0f) * Mathf.Deg2Rad;
        public static float gyro_tau = 500.0f;
        public static float gyro_bias_stability = 4.6f / 3600.0f * Mathf.Deg2Rad;
        public static Vector3 gyro_turn_on_bias = Vector3.zero;

        // Accelerometer: velocity random walk of 0.24 mg in m/s^2, bias stability of 36 ug in m/s^2, with an 800 s
        // correlation time
        public static float accel_vrw = 0.24f * Gravity / 1.0e3f;
        public static float accel_tau = 800.0f;
        public static float accel_bias_stability = 36.0e-6f * Gravity;
        public static Vector3 accel_turn_on_bias = Vector3.zero;

        // Internal IMU sample rate of 1 kHz
        public static float min_sample_time = 1.0f / 1000.0f;
    }
}
