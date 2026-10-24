using Microsoft.AspNetCore.Authorization;

namespace CasCap.Models;

/// <summary>Requires the user to administer the route-selected app, or any app when the route selects none.</summary>
public sealed class AppAdministratorRequirement : IAuthorizationRequirement;
