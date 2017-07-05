#ifndef CONFIG_H
#include <stdbool.h>
#include <sys/types.h>

#define CONFIG_H 1

typedef bool gpio_value;

struct receiver_gpio_config {
  uint32_t pin;
  gpio_value failsafe_value;
};

struct receiver_pwm_config {
  uint32_t pin;
  uint32_t pin_flags;
  gpio_value failsafe_value;
};

struct receiver_device_config { 
  char *device_identifier;
  int gpios_count;
  const struct receiver_gpio_config *gpios;
  int pwm_count;  
  const struct receiver_pwm_config *pwms;
};

extern const struct receiver_device_config device_config;

#endif 
