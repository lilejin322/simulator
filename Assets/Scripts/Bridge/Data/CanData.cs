// Car state that the AutoDrive car state sensor sends to the ROS bridges, which publish it as CarState. Only the
// plugins using it remain, so the fields are rebuilt from the members they use.

namespace Bridge.Data
{
    public class CanData
    {
        public float door_open_fl;
        public float door_open_fr;
        public float door_open_rl;
        public float door_open_rr;
        public float seatbelt_driver_unlatched;
        public float brake_pressed;
        public float wheel_speed_fl;
        public float wheel_speed_fr;
        public float wheel_speed_rl;
        public float wheel_speed_rr;
        public float gas_pedal;
        public float tc_disabled;
        public float steer_angle;
        public float steer_rate;
        public float gear;
        public float main_on;
        public float turn_signals;
        public float lka_state;
        public float ipas_state;
        public float steer_torque_driver;
        public float steer_torque_eps;
        public float set_speed;
        public float low_speed_lockout;
        public float cruise_active;
        public float cruise_state;
        public float brake_lights_acc;
        public float auto_high_beam;
        public float steering_angle_8;
        public float steering_angle_10;
        public float remotely_selected_map;
        public float error_code;
        public float io_values;
        public float status_flags;
        public float limit_flags;
        public float torque;
        public float motor_duty;
        public float current_amper;
        public float supply_voltage;
        public float switch_position;
        public float box_temprature;
        public float torque_a;
        public float torque_b;
    }
}
