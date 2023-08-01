using System;

namespace RT.Services;

/// <summary>Indicates that a service with the specified name or display name already exists.</summary>
public class ServiceAlreadyExistsException : Exception
{
    /// <summary>Constructor.</summary>
    public ServiceAlreadyExistsException()
        : base("A service with this name or display name already exists.")
    {
    }
}

/// <summary>Indicates that no service with the specified name is installed.</summary>
public class ServiceNotFoundException : Exception
{
    /// <summary>Constructor.</summary>
    public ServiceNotFoundException()
        : base("The specified service does not exist as an installed service.")
    {
    }
}
