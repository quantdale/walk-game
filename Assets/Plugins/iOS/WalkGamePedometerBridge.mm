#import <CoreMotion/CoreMotion.h>
#import <Foundation/Foundation.h>

/**
 * Narrow Core Motion bridge for Unity (MOBILE_ACTIVITY_INTEGRATION section 3).
 *
 * Contract:
 *  - Returns sensor FACTS only; all Vitality/reward logic lives in C#.
 *  - Historical queries are asynchronous: WG_QueryPedometerAsync returns a request
 *    id immediately and delivers ONE combined steps+distance result through the
 *    registered C# callback. The gameplay thread never blocks on a semaphore.
 *  - Live session updates keep the poll model (cheap cached doubles).
 *
 * Compiled into the Xcode project from Assets/Plugins/iOS automatically.
 */

static CMPedometer *wgPedometer = nil;
static dispatch_queue_t wgQueryQueue = nil;

// Live session accumulators (facts only).
static double wgLiveSteps = 0;
static double wgLiveDistanceMeters = 0;
static BOOL wgSessionActive = NO;

// C# callback: requestId identifies the query; steps < 0 signals failure.
typedef void (*WGQueryResultCallback)(int requestId, double steps, double distance, int errorCode);
static WGQueryResultCallback wgResultCallback = NULL;

static int wgNextRequestId = 0;

extern "C" {

int WG_IsPedometerAvailable(void)
{
    return [CMPedometer isStepCountingAvailable] ? 1 : 0;
}

int WG_GetAuthorizationStatus(void)
{
    if (@available(iOS 11.0, *)) {
        switch ([CMMotionActivityManager authorizationStatus]) {
            case CMAuthorizationStatusAllowed: return 3;   // granted
            case CMAuthorizationStatusDenied: return 2;    // denied
            case CMAuthorizationStatusNotDetermined: return 1; // not determined
            default: return 0;                             // unavailable
        }
    }
    return [CMPedometer isStepCountingAvailable] ? 3 : 0;
}

/// C# registers its marshalled delegate once during provider construction.
void WG_SetQueryResultCallback(WGQueryResultCallback callback)
{
    wgResultCallback = callback;
}

/// Asynchronous historical query over [startUnix, endUnix].
/// Returns a positive request id, or 0 when the query could not be started
/// (no callback registered / pedometer unavailable); -1 on invalid arguments.
int WG_QueryPedometerAsync(double startUnix, double endUnix)
{
    if (wgResultCallback == NULL) {
        return 0;
    }
    if (!(endUnix > startUnix)) {
        return -1;
    }
    if (![CMPedometer isStepCountingAvailable]) {
        return 0;
    }

    if (!wgPedometer) {
        wgPedometer = [[CMPedometer alloc] init];
    }
    if (!wgQueryQueue) {
        wgQueryQueue = dispatch_queue_create("com.walkgame.pedometer.queries", DISPATCH_QUEUE_SERIAL);
    }

    int requestId = ++wgNextRequestId;
    NSDate *start = [NSDate dateWithTimeIntervalSince1970:startUnix];
    NSDate *end = [NSDate dateWithTimeIntervalSince1970:endUnix];

    [wgPedometer queryPedometerDataFromDate:start toDate:end
        withHandler:^(CMPedometerData *data, NSError *error) {
            double steps = error ? -1 : (data.numberOfSteps ? data.numberOfSteps.doubleValue : 0);
            double distance = (error || !data.distance) ? -1 : data.distance.doubleValue;
            int errorCode = error ? (int)error.code : 0;

            // Marshal onto our serial queue so callback registration races and
            // overlapping queries stay ordered without touching Unity's main thread.
            dispatch_async(wgQueryQueue, ^{
                WGQueryResultCallback callback = wgResultCallback;
                if (callback != NULL) {
                    callback(requestId, steps, distance, errorCode);
                }
            });
        }];

    return requestId;
}

void WG_StartPedometerUpdates(double startUnix)
{
    if (!wgPedometer) {
        wgPedometer = [[CMPedometer alloc] init];
    }

    NSDate *start = [NSDate dateWithTimeIntervalSince1970:startUnix];
    wgLiveSteps = 0;
    wgLiveDistanceMeters = 0;
    wgSessionActive = YES;

    [wgPedometer startPedometerUpdatesFromDate:start
        withHandler:^(CMPedometerData *data, NSError *error) {
            if (!error && data) {
                wgLiveSteps = data.numberOfSteps.doubleValue;
                wgLiveDistanceMeters = data.distance ? data.distance.doubleValue : 0;
            }
        }];
}

// Polls live facts while a session runs (no cross-language callbacks required).
double WG_ReadLiveSteps(void)
{
    return wgSessionActive ? wgLiveSteps : 0;
}

double WG_ReadLiveDistance(void)
{
    return wgSessionActive ? wgLiveDistanceMeters : 0;
}

int WG_IsSessionActive(void)
{
    return wgSessionActive ? 1 : 0;
}

void WG_StopPedometerUpdates(void)
{
    wgSessionActive = NO;
    if (wgPedometer) {
        [wgPedometer stopPedometerUpdates];
    }
    wgLiveSteps = 0;
    wgLiveDistanceMeters = 0;
}

} // extern "C"
