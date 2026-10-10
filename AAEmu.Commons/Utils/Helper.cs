using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

// Source: https://www.cyberforum.ru/blogs/529033/blog3833.html
/*
 * This static class extends object and provides the CheckInterval method.
 * The method takes an interval in milliseconds and returns a bool.
 * It remembers the time of its last trigger and returns false until the interval has elapsed.
 * When it returns true, it records the current time and will not return true again until
 * at least the specified interval has passed.
 * The helper tracks trigger times separately for each object that calls CheckInterval.
 * If a class calls CheckInterval from multiple locations, each call gets its own timer.
 * This is done with the [CallerLineNumber] attribute, which passes the source line
 * from which CheckInterval was called.
 *
 *  This means code like the following also works correctly:
 *  void DoSomething()
 *  {
 *      // Run action 1 no more than once per second.
 *      if(this.CheckInterval(1000))
 *          DoAction1();      
 *      // Run action 2 no more than once every three seconds.
 *      if(this.CheckInterval(3000))
 *          DoAction2();
 *  }
 *
 * This static class also provides Triggered, which takes and returns a bool.
 * Triggered remembers the previous value and returns true when the value changes
 * from false to true.
 * As with the helper above, it stores the previous condition separately for each object
 * and each source line that calls it. Multiple calls from one method therefore each
 * have their own state flag:
 *
 * void CheckDistanceAndSayHello()
 *  {
 *      // Calculate the distance between the NPC and the player.
 *      var dist = CalcDist(player, npc);      
 *      // When the distance drops below one meter, say Hello once.
 *      if (this.Triggered(dist < 1))
 *          SayHello();      
 *      // When the distance rises above two meters, say Goodbye once.
 *      if (this.Triggered(dist > 2))
 *          SayBay();
 *  }
 */

namespace AAEmu.Commons.Utils;

public static class Helper
{
    private static Dictionary<Tuple<object, int>, DateTime> intervals = [];

    /// <summary>
    /// Returns true when at least the specified interval has elapsed since the previous trigger.
    /// </summary>
    public static bool CheckInterval(this object caller, int interval = 1000, [CallerLineNumber] int lineNumber = 0)
    {
        // Get the current time.
        var now = DateTime.UtcNow;

        // Build a key from the calling object and the source line that called this method.
        var key = new Tuple<object, int>(caller, lineNumber);

        // Get the next trigger time for this key.
        if (!intervals.TryGetValue(key, out var next))
            next = now; // If the dictionary has no time yet, allow the trigger now.

        // Has the next trigger time arrived?
        if (next > now)
            return false;

        // Set the next trigger time.
        intervals[key] = now.AddMilliseconds(interval);

        // The interval elapsed; return true.
        return true;
    }

    private static ConcurrentDictionary<Tuple<object, int>, bool> conditions = new();

    /// <summary>
    /// Returns true when the condition changes from false to true.
    /// </summary>
    public static bool Triggered(this object sender, bool condition, [CallerLineNumber] int lineNumber = 0)
    {
        // Build a key from the calling object and the source line that called this method.
        var key = new Tuple<object, int>(sender, lineNumber);

        // Get the previous condition value.
        if (!conditions.TryGetValue(key, out var old))
            old = false;

        // Store the new state.
        conditions[key] = condition;

        // Return true if the condition is true now but was false before.
        if (condition && !old)
            return true;

        // Otherwise, return false.
        return false;
    }
}
