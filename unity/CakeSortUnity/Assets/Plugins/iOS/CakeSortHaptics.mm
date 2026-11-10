// Native haptics for Cake Sort on iOS. Called from C# through DllImport("__Internal") (see Haptics.cs).
#import <UIKit/UIKit.h>

static UIImpactFeedbackGenerator *CSImpact(UIImpactFeedbackStyle style)
{
    static UIImpactFeedbackGenerator *generators[5];
    NSInteger index = (NSInteger)style;
    if (index < 0 || index > 4) index = 0;
    if (generators[index] == nil) generators[index] = [[UIImpactFeedbackGenerator alloc] initWithStyle:style];
    return generators[index];
}

extern "C" {

// kind: 0 light, 1 medium, 2 heavy, 3 soft, 4 rigid, 5 selection, 6 success, 7 warning, 8 error
void CakeSortHaptic(int kind)
{
    switch (kind)
    {
        case 0: [CSImpact(UIImpactFeedbackStyleLight) impactOccurred]; break;
        case 1: [CSImpact(UIImpactFeedbackStyleMedium) impactOccurred]; break;
        case 2: [CSImpact(UIImpactFeedbackStyleHeavy) impactOccurred]; break;
        case 3: [CSImpact(UIImpactFeedbackStyleSoft) impactOccurred]; break;
        case 4: [CSImpact(UIImpactFeedbackStyleRigid) impactOccurred]; break;
        case 5:
        {
            static UISelectionFeedbackGenerator *selection;
            if (selection == nil) selection = [[UISelectionFeedbackGenerator alloc] init];
            [selection selectionChanged];
            break;
        }
        default:
        {
            static UINotificationFeedbackGenerator *notification;
            if (notification == nil) notification = [[UINotificationFeedbackGenerator alloc] init];
            UINotificationFeedbackType type = kind == 6 ? UINotificationFeedbackTypeSuccess
                : kind == 7 ? UINotificationFeedbackTypeWarning : UINotificationFeedbackTypeError;
            [notification notificationOccurred:type];
            break;
        }
    }
}

// Wakes the Taptic Engine ahead of an expected tap (e.g. when a drag starts) so the landing tap isn't late.
void CakeSortHapticPrepare()
{
    [CSImpact(UIImpactFeedbackStyleMedium) prepare];
    [CSImpact(UIImpactFeedbackStyleLight) prepare];
}

}
