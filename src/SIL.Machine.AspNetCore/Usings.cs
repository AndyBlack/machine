global using System.Collections;
global using System.Collections.Concurrent;
global using System.Diagnostics;
global using System.Diagnostics.CodeAnalysis;
global using System.IO.Compression;
global using System.Linq.Expressions;
global using System.Net;
global using System.Reflection;
global using System.Runtime.CompilerServices;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Nodes;
global using Amazon;
global using Amazon.S3;
global using Amazon.S3.Model;
global using Amazon.S3.Transfer;
global using Grpc.Core;
global using Grpc.Core.Interceptors;
global using Grpc.Net.Client.Configuration;
global using Hangfire;
global using Hangfire.Mongo;
global using Hangfire.Mongo.Migration.Strategies;
global using Hangfire.Mongo.Migration.Strategies.Backup;
global using Microsoft.AspNetCore.Routing;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Diagnostics.HealthChecks;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using MongoDB.Driver;
global using MongoDB.Driver.Linq;
global using Nito.AsyncEx;
global using Polly;
global using SIL.DataAccess;
global using SIL.Machine.AspNetCore.Configuration;
global using SIL.Machine.AspNetCore.Models;
global using SIL.Machine.AspNetCore.Services;
global using SIL.Machine.AspNetCore.Utils;
global using SIL.Machine.Corpora;
global using SIL.Machine.Morphology.HermitCrab;
global using SIL.Machine.Tokenization;
global using SIL.Machine.Translation;
global using SIL.Machine.Translation.Thot;
global using SIL.Machine.Utils;
global using SIL.ObjectModel;
global using SIL.WritingSystems;
