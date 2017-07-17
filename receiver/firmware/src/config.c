#include "config.h"

const struct receiver_gpio_config gpio_config[] = {
  //     { 28, true},
};

const struct receiver_pwm_config pwm_config[] = {
  { 28, 0, 10000}
  //    { 29, 0, 0}
};

const receiver_device_config_t device_config = {
  "TestDevice",
  sizeof(gpio_config)/sizeof(gpio_config[0]), /* GPIO count */
  gpio_config,
  sizeof(pwm_config)/sizeof(pwm_config[0]), /* PWM count */
  pwm_config
};
