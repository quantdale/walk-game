package com.walkgame.sensors

import android.Manifest
import android.app.Activity
import android.content.Context
import android.content.pm.PackageManager
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.os.Build

/**
 * Narrow sensor-facts bridge for Unity (MOBILE_ACTIVITY_INTEGRATION section 5).
 *
 * Contract:
 *  - Returns raw sensor facts and permission state only.
 *  - NO Vitality math, NO reward logic, NO persistence here; C# domain owns those.
 *  - Cumulative counter semantics: steps since last reboot while the sensor is active.
 *
 * Uses only platform APIs (no androidx) so the plugin builds with any gradle template.
 */
class StepSensorBridge {

    companion object {
        private const val PERMISSION_REQUEST_CODE = 0x5747 // "WG"
    }

    private var appContext: Context? = null
    private var sensorManager: SensorManager? = null
    private var listenerRegistered = false

    private val listener = object : SensorEventListener {
        override fun onSensorChanged(event: SensorEvent) {
            if (event.sensor?.type == Sensor.TYPE_STEP_COUNTER) {
                val value = event.values.firstOrNull()?.toDouble() ?: return
                // Fail closed on corrupt payloads: never let NaN/Infinity/negative
                // values become a baseline or delta upstream (C# re-validates too).
                if (!value.isFinite() || value < 0.0) return
                synchronized(this@StepSensorBridge) {
                    latestCumulativeSteps = value
                }
            }
        }

        override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) {}
    }

    @Volatile
    private var latestCumulativeSteps: Double = Double.NaN

    /** Called by Unity right after construction with the current Unity player activity. */
    fun initialize(activity: Activity?) {
        appContext = activity?.applicationContext
        sensorManager = appContext?.getSystemService(Context.SENSOR_SERVICE) as? SensorManager
    }

    fun isStepCounterAvailable(): Boolean {
        return try {
            sensorManager?.getDefaultSensor(Sensor.TYPE_STEP_COUNTER) != null
        } catch (t: Throwable) {
            false
        }
    }

    /**
     * Permission state normalized to the shared contract:
     * 0 unavailable (< API 29 or missing sensor), 1 not determined, 2 denied, 3 granted.
     */
    fun getAuthorizationStatus(): Int {
        // A permission grant cannot make an emulator/device without the actual
        // counter sensor usable. Report unavailable first so the C# layer does not
        // present a misleading permission state or start a dead monitor.
        if (!isStepCounterAvailable()) return 0
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) return 3
        val ctx = appContext ?: return 0
        return try {
            val granted = ctx.checkSelfPermission(Manifest.permission.ACTIVITY_RECOGNITION) ==
                PackageManager.PERMISSION_GRANTED
            if (granted) 3 else 1
        } catch (t: Throwable) {
            0
        }
    }

    /**
     * Fires the system permission dialog through the hosting Activity. Results are
     * observed via getAuthorizationStatus(); C# drives all UX sequencing.
     */
    fun requestPermission(activity: Activity?) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) return
        val host = activity ?: return
        host.requestPermissions(arrayOf(Manifest.permission.ACTIVITY_RECOGNITION), PERMISSION_REQUEST_CODE)
    }

    /**
     * True when the system would show a rationale for ACTIVITY_RECOGNITION, which is
     * the earliest observable signal that the user answered the prompt with "deny".
     * Used by the C# layer to distinguish denied from not-determined; Android 11+
     * persistent denial (rationale stops appearing after repeated denials) is
     * additionally tracked process-side in C#.
     */
    fun shouldShowRequestRationale(activity: Activity?): Boolean {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.Q) return false
        val host = activity ?: return false
        return try {
            host.shouldShowRequestPermissionRationale(Manifest.permission.ACTIVITY_RECOGNITION)
        } catch (t: Throwable) {
            false
        }
    }

    @Synchronized
    fun startMonitoring(): Boolean {
        val manager = sensorManager ?: return false
        if (listenerRegistered) return true
        val sensor = manager.getDefaultSensor(Sensor.TYPE_STEP_COUNTER) ?: return false
        listenerRegistered = manager.registerListener(listener, sensor, SensorManager.SENSOR_DELAY_UI)
        return listenerRegistered
    }

    @Synchronized
    fun stopMonitoring() {
        if (!listenerRegistered) return
        sensorManager?.unregisterListener(listener)
        listenerRegistered = false
    }

    /** Latest cumulative counter value; NaN when nothing has been reported yet. */
    @Synchronized
    fun getCumulativeSteps(): Double {
        return latestCumulativeSteps
    }
}
