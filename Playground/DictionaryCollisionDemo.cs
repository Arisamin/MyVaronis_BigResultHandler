using System;
using System.Collections.Generic;

namespace Playground
{
    // Custom key type with configurable hash code and logging
    public class LoggingKey
    {
        public int KeyValue { get; }
        public int CustomHashCode { get; }
        public LoggingKey(int keyValue, int customHashCode)
        {
            KeyValue = keyValue;
            CustomHashCode = customHashCode;
        }
        public override int GetHashCode()
        {
            Console.WriteLine($"GetHashCode called for key {KeyValue}: returns {CustomHashCode}");
            return CustomHashCode;
        }
        public override bool Equals(object obj)
        {
            if (obj is LoggingKey other)
            {
                bool eq = KeyValue == other.KeyValue;
                Console.WriteLine($"Equals called: {KeyValue} == {other.KeyValue} => {eq}");
                return eq;
            }
            return false;
        }
        public override string ToString() => $"Key({KeyValue}, hash {CustomHashCode})";
    }

    public class DictionaryCollisionDemo
    {
        public static void Run()
        {
            var dict = new Dictionary<LoggingKey, string>();
            // Add three items as specified
            var key1000 = new LoggingKey(1000, 100);
            var key2000 = new LoggingKey(2000, 200);
            var key2001 = new LoggingKey(2001, 200);
            dict.Add(key1000, "1000");
            dict.Add(key2000, "2000");
            dict.Add(key2001, "2001");

            Console.WriteLine("Dictionary created with 3 items:");
            foreach (var kvp in dict)
                Console.WriteLine($"  {kvp.Key}: {kvp.Value}");

            Console.WriteLine("\n--- Retrieving value for key 1000 ---");
            var lookup1000 = new LoggingKey(1000, 100);
            Console.WriteLine($"Value: {dict[lookup1000]}");

            Console.WriteLine("\n--- Retrieving value for key 2001 ---");
            var lookup2001 = new LoggingKey(2001, 200);
            Console.WriteLine($"Value: {dict[lookup2001]}");
        }
    }
}
