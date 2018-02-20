namespace AzureChaos.Core.Constants
{
    /// <summary>Storage table is not allowing to store the partition key with flashes.
    /// so "delimeters" used to store the resource id as a partition key by replacing the forward slash into exclamatory and vice versa(when reading from the table).
    /// </summary>
    public class Delimeters
    {
        public static char Exclamatory = '!';
        public static char ForwardSlash = '/';
        public static char At = '@';
    }
}