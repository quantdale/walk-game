#import <CoreMotion/CoreMotion.h>
#import <Foundation/Foundation.h>

/**
 * Narrow Core Motion bridge for Unity (MOBILE_ACTIVITY_INTEGRATION section 3).
 *
 * Contract:
 *  - Returns sensor FACTS only; all Vitality/reward logic lives in C#.
 *  - Historical queries are bounded by Core Motion's 7-day availability; the C#
 *    layer owns lastSuccessfulSyncUtc and never re-credits intervals.
 *
 * Compiled into the Xcode project from Assets/Plugins/iOS automatically.
 */

static CMPedometer *wgPedometer = nil;
static NSTimer *wgPollTimer = nil;

// Live session accumulators (facts only).
static double wgLiveSteps = 0;
static double wgLiveDistanceMeters = 0;
static BOOL wgSessionActive = NO;

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
    return [CMPedometer isStepCountingAvailable] ? 1 : 0;
}

// Returns steps for [startUnix, endUnix]; -1 on error. Bounded by system history.
double WG_QueryPedometerSteps(double startUnix, double endUnix)
{
    if (!wgPedometer) {
        wgPedometer = [[CMPedometer alloc] init];
    }

    NSDate *start = [NSDate dateWithTimeIntervalSince1970:startUnix];
    NSDate *end = [NSDate dateWithTimeIntervalSince1970:endUnix];

    __block double steps = -1;
    __block BOOL done = NO;
    dispatch_semaphore_t semaphore = dispatch_semaphore_create(0);

    [wgPedometer queryPedometerDataFromDate:start toDate:end
        withHandler:^(CMPedometerData *data, NSError *error) {
            if (!error && data.numberOfSteps) {
                steps = data.numberOfSteps.doubleValue;
            }
            done = YES;
            dispatch_semaphore_signal(semaphore);
        }];

    // CMPedometer queries complete quickly; bounded wait keeps the interop simple.
    dispatch_semaphore_wait(semaphore, dispatch_time(DISPATCH_TIME_NOW, 4 * NSEC_PER_SEC));
    return steps;
}

// Returns estimated walking+running distance in meters for the interval; -1 on error.
double WG_QueryPedometerDistance(double startUnix, double endUnix)
{
    if (!wgPedometer) {
        wgPedometer = [[CMPedometer alloc] init];
    }

    NSDate *start = [NSDate dateWithTimeIntervalSince1970:startUnix];
    NSDate *end = [NSDate dateWithTimeIntervalSince1970:endUnix];

    __block double distance = -1;
    dispatch_semaphore_t semaphore = dispatch_semaphore_create(0);

    [wgPedometer queryPedometerDataFromDate:start toDate:end
        withHandler:^(CMPedometerData *data, NSError *error) {
            if (!error && data.distance) {
                distance = data.distance.doubleValue;
            }
            dispatch_semaphore_signal(semaphore);
        }];

    dispatch_semaphore_wait(semaphore, dispatch_time(DISPATCH_TIME_NOW, 4 * NSEC_PER_SEC));
    return distance;
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
}

} // extern "C"
