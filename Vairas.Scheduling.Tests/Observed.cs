extern alias SchedulingApi;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using Vairas.Scheduling.Business;
using Vairas.Scheduling.Domain;
using SchedulingProgram = SchedulingApi::Program;
using AvailabilityAudit = SchedulingApi::Vairas.Scheduling.Middleware.AvailabilityAudit;
using ISchedulingActorFactory = SchedulingApi::Vairas.Scheduling.Integrations.ISchedulingActorFactory;
namespace Vairas.Scheduling.Tests;

/// <summary>One observed response; volatile values are normalized before equivalence comparison.</summary>
internal sealed record Observed(int Status, int RawLength, string Body, string Headers);
