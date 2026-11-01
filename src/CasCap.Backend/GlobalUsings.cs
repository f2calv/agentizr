global using CasCap.Abstractions;
global using CasCap.AgentRuntime.Evaluation.Extensions;
global using CasCap.AgentRuntime.Evaluation.Models;
global using CasCap.AgentRuntime.Evaluation.Services;
global using CasCap.AgentRuntime.Execution.Extensions;
global using CasCap.AgentRuntime.Execution.Models;
global using CasCap.Common.Abstractions;
global using CasCap.Common.Models;
global using CasCap.Models;
#if DEBUG
global using CasCap.AgentRuntime.Contracts.RuntimeConfiguration;
global using CasCap.RuntimeConfiguration.Models;
#endif
global using CasCap.Services;
global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Hosting;
global using Microsoft.AspNetCore.Http;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using System.ComponentModel.DataAnnotations;
global using System.Net.Http.Json;
