namespace typegen.Builder;

public class TypeNotSupportedException(string type) : Exception($"You should not see this error. Please report it as a bug. Type '{type}' is not supported");