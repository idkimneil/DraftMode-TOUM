using System;

namespace DraftModeTOUM.Tests
{
    public static class Check
    {
        public static int Passed;
        public static int Failures;

        public static void True(bool condition, string message)
        {
            if (condition) Passed++;
            else { Failures++; Console.WriteLine("  FAIL: " + message); }
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            if (Equals(expected, actual)) Passed++;
            else { Failures++; Console.WriteLine($"  FAIL: {message} (expected {expected}, got {actual})"); }
        }
    }
}
