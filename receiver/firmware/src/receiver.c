/**
 * Copyright (c) 2012 - 2017, Nordic Semiconductor ASA
 * 
 * All rights reserved.
 * 
 * Redistribution and use in source and binary forms, with or without modification,
 * are permitted provided that the following conditions are met:
 * 
 * 1. Redistributions of source code must retain the above copyright notice, this
 *    list of conditions and the following disclaimer.
 * 
 * 2. Redistributions in binary form, except as embedded into a Nordic
 *    Semiconductor ASA integrated circuit in a product or a software update for
 *    such product, must reproduce the above copyright notice, this list of
 *    conditions and the following disclaimer in the documentation and/or other
 *    materials provided with the distribution.
 * 
 * 3. Neither the name of Nordic Semiconductor ASA nor the names of its
 *    contributors may be used to endorse or promote products derived from this
 *    software without specific prior written permission.
 * 
 * 4. This software, with or without modification, must only be used with a
 *    Nordic Semiconductor ASA integrated circuit.
 * 
 * 5. Any software provided in binary form under this license must not be reverssed
 *    engineered, decompiled, modified and/or disassembled.
 * 
 * THIS SOFTWARE IS PROVIDED BY NORDIC SEMICONDUCTOR ASA "AS IS" AND ANY EXPRESS
 * OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
 * OF MERCHANTABILITY, NONINFRINGEMENT, AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL NORDIC SEMICONDUCTOR ASA OR CONTRIBUTORS BE
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE
 * GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
 * HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
 * LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT
 * OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 * 
 */
/* Attention!
*  To maintain compliance with Nordic Semiconductor ASA's Bluetooth profile
*  qualification listings, this section of source code must not be modified.
*/

#include "receiver.h"
#include <string.h>
#include "nordic_common.h"
#include "ble.h"
#include "ble_srv_common.h"
#include "app_util.h"
#include "nrf_gpio.h"
#include "nrf_drv_pwm.h"
#include "app_util_platform.h"
#include "app_timer.h"

// XXX all guids are temporary

#define OPCODE_LENGTH 1                                                             /**< Length of opcode inside Cycling Speed and Cadence Measurement packet. */
#define HANDLE_LENGTH 2                                                             /**< Length of handle inside Cycling Speed and Cadence Measurement packet. */
#define MAX_CSCM_LEN  (BLE_GATT_ATT_MTU_DEFAULT - OPCODE_LENGTH - HANDLE_LENGTH)    /**< Maximum size of a transmitted Cycling Speed and Cadence Measurement. */

#define BLE_RECEIVER_WATCHDOG_TICKS APP_TIMER_TICKS(1000)

// XXX should all be part of the receiver struct

nrf_drv_pwm_t g_pwm_instance = NRF_DRV_PWM_INSTANCE(0);
nrf_pwm_values_individual_t g_pwm_seq_values[1];
nrf_pwm_values_individual_t g_pwm_seq_values_new[1];

nrf_pwm_sequence_t const g_pwm_seq = {
  .values.p_individual = g_pwm_seq_values,
  .length = NRF_PWM_VALUES_LENGTH(g_pwm_seq_values),
  .repeats = 0,
  .end_delay = 0
};

bool g_pwm_new_values = false;

bool g_outstanding_watchdog = false;
bool g_heartbeat_received = false;

APP_TIMER_DEF(g_watchdog_timer_id);

void ble_receiver_gpio_set(ble_receiver_t *p_receiver, gpio_value *vals)
{
  for (int i = 0; i < p_receiver->config->gpios_count; i++) {
    nrf_gpio_pin_write(p_receiver->config->gpios[i].pin, vals[i]);
  }
}

void ble_receiver_gpio_set_validate(ble_receiver_t *p_receiver, uint8_t * p_data, uint16_t length)
{
  if (length != (p_receiver->config->gpios_count * sizeof(gpio_value))) {
    return;
  }
  ble_receiver_gpio_set(p_receiver, (gpio_value *) p_data);
}
void ble_receiver_gpio_set_failsafe(ble_receiver_t *p_receiver)
{
  gpio_value vals[32];

  for (int i = 0; i < p_receiver->config->gpios_count; i++) {
    vals[i]= p_receiver->config->gpios[i].failsafe_value;
  }
  ble_receiver_gpio_set(p_receiver, vals);
}

void ble_receiver_gpio_init(ble_receiver_t *p_receiver) {
  if (p_receiver->config->gpios_count == 0) {
    return;
  }

  for (int i = 0; i < p_receiver->config->gpios_count; i++) {
    nrf_gpio_cfg_output(p_receiver->config->gpios[i].pin);
  }
  ble_receiver_gpio_set_failsafe(p_receiver);
}

void ble_receiver_pwm_set(ble_receiver_t *p_receiver, pwm_value *vals)
{
  g_pwm_seq_values_new[0].channel_0 = *vals++;
  g_pwm_seq_values_new[0].channel_1 = *vals++;
  g_pwm_seq_values_new[0].channel_2 = *vals++;
  g_pwm_seq_values_new[0].channel_3 = *vals++;
  
  g_pwm_new_values = true;
}

void ble_receiver_pwm_set_validate(ble_receiver_t *p_receiver, uint8_t * p_data, uint16_t length)
{
  if (length != (p_receiver->config->pwm_count * sizeof(pwm_value))) {
    return;
  }
  ble_receiver_pwm_set(p_receiver, (pwm_value *) p_data);
}

void ble_receiver_pwm_update(void)
{
  if (g_pwm_new_values == false) {
    return;
  }

  g_pwm_seq_values[0] = g_pwm_seq_values_new[0];
  g_pwm_new_values = false;
}

void ble_receiver_pwm_start(ble_receiver_t *p_receiver)
{
  nrf_drv_pwm_simple_playback(&g_pwm_instance,
			      &g_pwm_seq,
			      1,
			      NRF_DRV_PWM_FLAG_LOOP);
#if NOT_YET
			      //			      NRF_DRV_PWM_FLAG_SIGNAL_END_SEQ0 |
			      //			      NRF_DRV_PWM_FLAG_SIGNAL_END_SEQ1);
#endif
}

void pwm_handler(nrf_drv_pwm_evt_type_t event_type)
{

  if (g_pwm_new_values == false) {
    return;
  }

  if ((event_type == NRF_DRV_PWM_EVT_END_SEQ0) || (event_type == NRF_DRV_PWM_EVT_END_SEQ1)) {
    ble_receiver_pwm_update();
  }
}

void ble_receiver_pwm_set_failsafe(ble_receiver_t *p_receiver)
{
  pwm_value vals[4];

  for (int i = 0; i < 4; i++) {
    if (i < p_receiver->pwm_count) {
      vals[i] = p_receiver->config->pwms[i].pin;
    } else {
      vals[i] = 0;
    }
  }
  ble_receiver_pwm_set(p_receiver, vals);
}

// XXX synchronization problems? 
void ble_receiver_watchdog(ble_receiver_t *p_receiver)
{
#if NOT_YET
  ble_receiver_pwm_set_failsafe(p_receiver);
#endif
  ble_receiver_gpio_set_failsafe(p_receiver);
}

void ble_receiver_start_watchdog(ble_receiver_t *p_receiver)
{
  if (g_outstanding_watchdog == false) {
    g_outstanding_watchdog = true;
    app_timer_start(g_watchdog_timer_id, BLE_RECEIVER_WATCHDOG_TICKS, p_receiver);
  }
}


void ble_receiver_heartbeat_received(ble_receiver_t *p_receiver)
{
  g_heartbeat_received = true;
}

//
// Simplistic watchdog/failsafe logic.
// * When connected, issue watchdog timer oneshot.
// * If watchdog event occurs, check for heartbeat, otherwise failsafe.
// * If watchdog event occurs and connected, issue new oneshot.
// * On disconnect event, issue oneshot.
//
// Potential improvements:
// * new PWM step down sequence
// * more tolerance for missed hearbeats
// * restart oneshot on heartbeat receive (stop existing, start new)
// * treat all input as heartbeat reception
//

void ble_receiver_watchdog_handler(void *p_context)
{
  ble_receiver_t *p_receiver = p_context;

  if (g_heartbeat_received != true) {
    ble_receiver_watchdog(p_receiver);
  }
  g_heartbeat_received = false;
  g_outstanding_watchdog = false;

  if (p_receiver->conn_handle != BLE_CONN_HANDLE_INVALID) {
    ble_receiver_start_watchdog(p_receiver);
  }
}

/**@brief Function for handling the Connect event.
 *
 * @param[in]   p_receiver      Cycling Speed and Cadence Service structure.
 * @param[in]   p_ble_evt   Event received from the BLE stack.
 */
static void on_connect(ble_receiver_t * p_receiver, ble_evt_t * p_ble_evt)
{
    p_receiver->conn_handle = p_ble_evt->evt.gap_evt.conn_handle;
    ble_receiver_start_watchdog(p_receiver);
}


/**@brief Function for handling the Disconnect event.
 *
 * @param[in]   p_receiver      Cycling Speed and Cadence Service structure.
 * @param[in]   p_ble_evt   Event received from the BLE stack.
 */
static void on_disconnect(ble_receiver_t * p_receiver, ble_evt_t * p_ble_evt)
{
    UNUSED_PARAMETER(p_ble_evt);
    p_receiver->conn_handle = BLE_CONN_HANDLE_INVALID;
    ble_receiver_start_watchdog(p_receiver);
}

#if NOT_YET
/**@brief Function for handling write events to the RECEIVER Measurement characteristic.
 *
 * @param[in]   p_receiver        Cycling Speed and Cadence Service structure.
 * @param[in]   p_evt_write   Write event received from the BLE stack.
 */
static void on_meas_cccd_write(ble_receiver_t * p_receiver, ble_gatts_evt_write_t * p_evt_write)
{
    if (p_evt_write->len == 2)
    {
        // CCCD written, update notification state
        if (p_receiver->evt_handler != NULL)
        {
            ble_receiver_evt_t evt;

            if (ble_srv_is_notification_enabled(p_evt_write->data))
            {
                evt.evt_type = BLE_RECEIVER_EVT_NOTIFICATION_ENABLED;
            }
            else
            {
                evt.evt_type = BLE_RECEIVER_EVT_NOTIFICATION_DISABLED;
            }

            p_receiver->evt_handler(p_receiver, &evt);
        }
    }
}
#endif

/**@brief Function for handling the Write event.
 *
 * @param[in]   p_receiver      Cycling Speed and Cadence Service structure.
 * @param[in]   p_ble_evt   Event received from the BLE stack.
 */
static void on_write(ble_receiver_t * p_receiver, ble_evt_t * p_ble_evt)
{
    ble_gatts_evt_write_t * p_evt_write = &p_ble_evt->evt.gatts_evt.params.write;

#if NOT_YET
    if (p_evt_write->handle == p_receiver->meas_handles.cccd_handle)
    {
        on_meas_cccd_write(p_receiver, p_evt_write);
    }
    else if
#endif
    if (p_evt_write->handle == p_receiver->heartbeat_handles.value_handle) {
      ble_receiver_heartbeat_received(p_receiver);
    } else if (p_evt_write->handle == p_receiver->gpio_handles.value_handle) {
      ble_receiver_gpio_set_validate(p_receiver, p_evt_write->data, p_evt_write->len);
    } else if (p_evt_write->handle == p_receiver->pwm_handles.value_handle) {
      ble_receiver_pwm_set_validate(p_receiver, p_evt_write->data, p_evt_write->len);
    } 
}

void ble_receiver_on_ble_evt(ble_receiver_t * p_receiver, ble_evt_t * p_ble_evt)
{
    switch (p_ble_evt->header.evt_id)
    {
        case BLE_GAP_EVT_CONNECTED:
            on_connect(p_receiver, p_ble_evt);
            break;

        case BLE_GAP_EVT_DISCONNECTED:
            on_disconnect(p_receiver, p_ble_evt);
            break;

        case BLE_GATTS_EVT_WRITE:
            on_write(p_receiver, p_ble_evt);
            break;

        default:
            // No implementation needed.
            break;
    }
}
#if NOT_YET

/**@brief Function for adding CSC Measurement characteristics.
 *
 * @param[in]   p_receiver        Cycling Speed and Cadence Service structure.
 * @param[in]   p_receiver_init   Information needed to initialize the service.
 *
 * @return      NRF_SUCCESS on success, otherwise an error code.
 */
static uint32_t csc_measurement_char_add(ble_receiver_t * p_receiver, const ble_receiver_init_t * p_receiver_init)
{
    ble_gatts_char_md_t char_md;
    ble_gatts_attr_md_t cccd_md;
    ble_gatts_attr_t    attr_char_value;
    ble_uuid_t          ble_uuid;
    ble_gatts_attr_md_t attr_md;
    ble_receiver_meas_t     initial_scm;
    uint8_t             encoded_scm[MAX_CSCM_LEN];
    memset(&cccd_md, 0, sizeof(cccd_md));

    BLE_GAP_CONN_SEC_MODE_SET_OPEN(&cccd_md.read_perm);
    cccd_md.write_perm = p_receiver_init->csc_meas_attr_md.cccd_write_perm;
    cccd_md.vloc       = BLE_GATTS_VLOC_STACK;

    memset(&char_md, 0, sizeof(char_md));

    char_md.char_props.notify = 1;
    char_md.p_char_user_desc  = NULL;
    char_md.p_char_pf         = NULL;
    char_md.p_user_desc_md    = NULL;
    char_md.p_cccd_md         = &cccd_md;
    char_md.p_sccd_md         = NULL;

    BLE_UUID_BLE_ASSIGN(ble_uuid, BLE_UUID_CSC_MEASUREMENT_CHAR);

    memset(&attr_md, 0, sizeof(attr_md));

    BLE_GAP_CONN_SEC_MODE_SET_NO_ACCESS(&attr_md.read_perm );

    BLE_GAP_CONN_SEC_MODE_SET_NO_ACCESS(&attr_md.write_perm);
    attr_md.vloc       = BLE_GATTS_VLOC_STACK;
    attr_md.rd_auth    = 0;
    attr_md.wr_auth    = 0;
    attr_md.vlen       = 1;

    memset(&attr_char_value, 0, sizeof(attr_char_value));

    attr_char_value.p_uuid       = &ble_uuid;
    attr_char_value.p_attr_md    = &attr_md;
    attr_char_value.init_len     = csc_measurement_encode(p_receiver, &initial_scm, encoded_scm);
    attr_char_value.init_offs    = 0;
    attr_char_value.max_len      = MAX_CSCM_LEN;
    attr_char_value.p_value      = encoded_scm;

    return sd_ble_gatts_characteristic_add(p_receiver->service_handle,
                                           &char_md,
                                           &attr_char_value,
                                           &p_receiver->meas_handles);
}
#endif

/**@brief Function for adding receiver device id characteristics.
 *
 * @param[in]   p_receiver        Receiver Service structure.
 * @param[in]   p_receiver_init   Information needed to initialize the service.
 *
 * @return      NRF_SUCCESS on success, otherwise an error code.
 */
static uint32_t receiver_deviceid_char_add(ble_receiver_t * p_receiver, const ble_receiver_init_t * p_receiver_init)
{
    ble_gatts_char_md_t char_md;
    ble_gatts_attr_t    attr_char_value;
    ble_uuid_t          ble_uuid;
    ble_gatts_attr_md_t attr_md;
    uint8_t             init_value_len;

    memset(&char_md, 0, sizeof(char_md));

    char_md.char_props.read  = 1;
    char_md.p_char_user_desc = NULL;
    char_md.p_char_pf        = NULL;
    char_md.p_user_desc_md   = NULL;
    char_md.p_cccd_md        = NULL;
    char_md.p_sccd_md        = NULL;

    BLE_UUID_BLE_ASSIGN(ble_uuid, BLE_UUID_CSC_FEATURE_CHAR);

    memset(&attr_md, 0, sizeof(attr_md));

    BLE_GAP_CONN_SEC_MODE_SET_OPEN(&attr_md.read_perm);  
    BLE_GAP_CONN_SEC_MODE_SET_NO_ACCESS(&attr_md.write_perm);
    attr_md.vloc       = BLE_GATTS_VLOC_STACK;
    attr_md.rd_auth    = 0;
    attr_md.wr_auth    = 0;
    attr_md.vlen       = 0;

    memset(&attr_char_value, 0, sizeof(attr_char_value));

    init_value_len = strlen(p_receiver_init->config->device_identifier);

    attr_char_value.p_uuid    = &ble_uuid;
    attr_char_value.p_attr_md = &attr_md;
    attr_char_value.init_len  = init_value_len;
    attr_char_value.init_offs = 0;
    attr_char_value.max_len   = init_value_len;
    attr_char_value.p_value   = (void *)p_receiver_init->config->device_identifier;

    return sd_ble_gatts_characteristic_add(p_receiver->service_handle,
                                           &char_md,
                                           &attr_char_value,
                                           &p_receiver->deviceid_handles);
}

static uint32_t receiver_write_char_add(ble_receiver_t * p_receiver, const ble_receiver_init_t * p_receiver_init, uint16_t uuid, ble_gatts_char_handles_t *const 	p_handles)
{
    ble_gatts_char_md_t char_md;
    ble_gatts_attr_t    attr_char_value;
    ble_uuid_t          ble_uuid;
    ble_gatts_attr_md_t attr_md;

    memset(&char_md, 0, sizeof(char_md));

    char_md.char_props.write_wo_resp = 1;
    char_md.p_char_user_desc         = NULL;
    char_md.p_char_pf                = NULL;
    char_md.p_user_desc_md           = NULL;
    char_md.p_cccd_md                = NULL;
    char_md.p_sccd_md                = NULL;

    // XXX temp
    BLE_UUID_BLE_ASSIGN(ble_uuid, uuid);

    memset(&attr_md, 0, sizeof(attr_md));

    BLE_GAP_CONN_SEC_MODE_SET_OPEN(&attr_md.read_perm);
    BLE_GAP_CONN_SEC_MODE_SET_OPEN(&attr_md.write_perm);

    attr_md.vloc    = BLE_GATTS_VLOC_STACK;
    attr_md.rd_auth = 0;
    attr_md.wr_auth = 0;
    attr_md.vlen    = 1;

    memset(&attr_char_value, 0, sizeof(attr_char_value));

    attr_char_value.p_uuid    = &ble_uuid;
    attr_char_value.p_attr_md = &attr_md;
    attr_char_value.init_len  = 1;
    attr_char_value.init_offs = 0;
    attr_char_value.max_len   = 1;

    return sd_ble_gatts_characteristic_add(p_receiver->service_handle,
                                           &char_md,
                                           &attr_char_value,
                                           p_handles);
}

uint32_t ble_receiver_pwm_init(ble_receiver_t *p_receiver)
{
  uint32_t err_code;
  nrf_drv_pwm_config_t pwm_config = {
    .irq_priority = APP_IRQ_PRIORITY_LOW,
    .base_clock   = NRF_PWM_CLK_1MHz,
    .count_mode   = NRF_PWM_MODE_UP,
    .top_value    = 1000,
    .load_mode    = NRF_PWM_LOAD_INDIVIDUAL,
    .step_mode    = NRF_PWM_STEP_AUTO
  };
  int pwm_count;

  if (p_receiver->config->pwm_count == 0) {
    return NRF_SUCCESS;
  }

  //
  // Initialize PWM channel data
  //

  pwm_count = p_receiver->config->pwm_count;
  if (pwm_count > 4) {
    pwm_count = 4;
  }
  p_receiver->pwm_count = pwm_count;

  for (int i = 0; i < 4; i++) {
    if (i < pwm_count) {
      pwm_config.output_pins[i] = p_receiver->config->pwms[i].pin;
    } else {
      pwm_config.output_pins[i] = NRF_DRV_PWM_PIN_NOT_USED;
    }
  }

  err_code = nrf_drv_pwm_init(&g_pwm_instance, &pwm_config, NULL);
  // XXX pwm_handler
  APP_ERROR_CHECK(err_code);
  
  ble_receiver_pwm_set_failsafe(p_receiver);
  ble_receiver_pwm_update();

  ble_receiver_pwm_start(p_receiver);

  return NRF_SUCCESS;
}

uint32_t ble_receiver_init(ble_receiver_t * p_receiver, ble_receiver_init_t *p_receiver_init)
{
    uint32_t             err_code;
    ble_uuid_t           ble_uuid;

    // Initialize service structure
#if NOT_YET
    p_receiver->evt_handler = p_receiver_init->evt_handler;
#endif
    p_receiver->conn_handle = BLE_CONN_HANDLE_INVALID;
    p_receiver->config      = p_receiver_init->config;

    ble_receiver_gpio_init(p_receiver);
    ble_receiver_pwm_init(p_receiver);

    // Create watchdog timer
    err_code = app_timer_create(&g_watchdog_timer_id,
                                APP_TIMER_MODE_SINGLE_SHOT,
                                ble_receiver_watchdog_handler);

    // Add service
    BLE_UUID_BLE_ASSIGN(ble_uuid, BLE_UUID_CYCLING_SPEED_AND_CADENCE);

    err_code = sd_ble_gatts_service_add(BLE_GATTS_SRVC_TYPE_PRIMARY,
                                        &ble_uuid,
                                        &p_receiver->service_handle);

    if (err_code != NRF_SUCCESS)
    {
        return err_code;
    }

    // Add device id charactersistic
    err_code = receiver_deviceid_char_add(p_receiver, p_receiver_init);
    APP_ERROR_CHECK(err_code);

    // Add heartbeat charactersistic
    err_code = receiver_write_char_add(p_receiver,
				       p_receiver_init,
				       BLE_UUID_HEART_RATE_MEASUREMENT_CHAR,
				       &p_receiver->heartbeat_handles);
    APP_ERROR_CHECK(err_code);
    // Add gpio charactersistic
    err_code = receiver_write_char_add(p_receiver,
				       p_receiver_init,
				       BLE_UUID_BLOOD_PRESSURE_SERVICE,
				       &p_receiver->gpio_handles);
    APP_ERROR_CHECK(err_code);
    // Add pwm charactersistic
    err_code = receiver_write_char_add(p_receiver,
				       p_receiver_init,
				       BLE_UUID_GLUCOSE_SERVICE,
				       &p_receiver->pwm_handles);
    APP_ERROR_CHECK(err_code);

#if 0
    // Add Sensor Location characteristic (optional)
    if (p_receiver_init->sensor_location != NULL)
    {
        err_code = csc_sensor_loc_char_add(p_receiver, p_receiver_init);

        if (err_code != NRF_SUCCESS)
        {
            return err_code;
        }
    }
#endif
    return NRF_SUCCESS;
}

#if NOT_YET

uint32_t ble_receiver_measurement_send(ble_receiver_t * p_receiver, ble_receiver_meas_t * p_measurement)
{
    uint32_t err_code;

    // Send value if connected and notifying
    if (p_receiver->conn_handle != BLE_CONN_HANDLE_INVALID)
    {
        uint8_t                encoded_csc_meas[MAX_CSCM_LEN];
        uint16_t               len;
        uint16_t               hvx_len;
        ble_gatts_hvx_params_t hvx_params;

        len     = csc_measurement_encode(p_receiver, p_measurement, encoded_csc_meas);
        hvx_len = len;

        memset(&hvx_params, 0, sizeof(hvx_params));

        hvx_params.handle = p_receiver->meas_handles.value_handle;
        hvx_params.type   = BLE_GATT_HVX_NOTIFICATION;
        hvx_params.offset = 0;
        hvx_params.p_len  = &hvx_len;
        hvx_params.p_data = encoded_csc_meas;

        err_code = sd_ble_gatts_hvx(p_receiver->conn_handle, &hvx_params);
        if ((err_code == NRF_SUCCESS) && (hvx_len != len))
        {
            err_code = NRF_ERROR_DATA_SIZE;
        }
    }
    else
    {
        err_code = NRF_ERROR_INVALID_STATE;
    }

    return err_code;
}
#endif
