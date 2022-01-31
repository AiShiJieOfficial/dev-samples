using System;

internal static class ExtensionMethods
{
    internal static string[] Split(this string s, string delimiter, StringSplitOptions options = StringSplitOptions.None)
    {
        return s.Split(new string[] { delimiter }, options);
    }
}
