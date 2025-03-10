namespace SIL.Machine.AspNetCore.Utils
{
    /// </summary> This exception is thrown when an unbuilt engine is requested to perform an action that requires it being built <summary>
    public class EngineNotBuiltException : Exception
    {
        public EngineNotBuiltException(string message)
            : base(message) { }
    }
}
