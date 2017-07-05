#include "config.h"

const struct receiver_gpio_config gpio_config[] = {
  { 11, false}
};

const struct receiver_pwm_config pwm_config[] = {
  { 17, 0, 0}
};



const struct receiver_device_config device_config = {
  "TestDevice",
  sizeof(gpio_config)/sizeof(gpio_config[0]), /* GPIO count */
  gpio_config,
  sizeof(pwm_config)/sizeof(pwm_config[0]), /* PWM count */
  pwm_config
};
