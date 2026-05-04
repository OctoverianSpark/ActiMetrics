using System;
using System.IO;
using System.Reflection;

class Program
{
    static void Main()
    {
        try
        {
            var sn = new System.Reflection.StrongNameKeyPair(new byte[1024]);
            using (var stream = File.Create("ActiMetrics.snk"))
            {
                var keyBytes = sn.PublicKey;
                stream.Write(keyBytes, 0, keyBytes.Length);
            }
            Console.WriteLine("Strong name key generated: ActiMetrics.snk");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
