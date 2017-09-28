﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using NuGet.Protocol;
using NuGet.Versioning;

namespace NuGetGallery.Security
{
    /// <summary>
    /// This code should be removed soon: https://github.com/NuGet/Engineering/issues/800
    /// User security policy that requires a minimum client version in order to push packages.
    /// </summary>
    public class RequireMinClientVersionForPushPolicy : UserSecurityPolicyHandler
    {
        public const string PolicyName = nameof(RequireMinClientVersionForPushPolicy);

        public class State
        {
            [JsonProperty("v")]
            [JsonConverter(typeof(NuGetVersionConverter))]
            public NuGetVersion MinClientVersion { get; set; }
        }

        public RequireMinClientVersionForPushPolicy()
            : base(PolicyName, SecurityPolicyAction.PackagePush)
        {
        }

        /// <summary>
        /// Create a user security policy that requires a minimum client version.
        /// </summary>
        public static UserSecurityPolicy CreatePolicy(string subscription, NuGetVersion minClientVersion)
        {
            var value = JsonConvert.SerializeObject(new State() {
                MinClientVersion = minClientVersion
            });

            return new UserSecurityPolicy(PolicyName, subscription, value);
        }

        /// <summary>
        /// In case of multiple, select the max of the minimum required client versions.
        /// </summary>
        private NuGetVersion GetMaxOfMinClientVersions(UserSecurityPolicyEvaluationContext context)
        {
            var policyStates = context.Policies
                .Where(p => !string.IsNullOrEmpty(p.Value))
                .Select(p => JsonConvert.DeserializeObject<State>(p.Value));
            return policyStates.Max(s => s.MinClientVersion);
        }

        /// <summary>
        /// Get the current client version from the request.
        /// </summary>
        private NuGetVersion GetClientVersion(UserSecurityPolicyEvaluationContext context)
        {
            var clientVersionString = context.HttpContext.Request?.Headers[Constants.ClientVersionHeaderName];

            NuGetVersion clientVersion;
            return NuGetVersion.TryParse(clientVersionString, out clientVersion) ? clientVersion : null;
        }

        /// <summary>
        /// Get the current protocol version from the request.
        /// </summary>
        private NuGetVersion GetProtocolVersion(UserSecurityPolicyEvaluationContext context)
        {
            var protocolVersionString = context.HttpContext.Request?.Headers[Constants.NuGetProtocolHeaderName];

            NuGetVersion protocolVersion;
            return NuGetVersion.TryParse(protocolVersionString, out protocolVersion) ? protocolVersion : null;
        }

        /// <summary>
        /// Evaluate if this security policy is met.
        /// </summary>
        public override SecurityPolicyResult Evaluate(UserSecurityPolicyEvaluationContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var minClientVersion = GetMaxOfMinClientVersions(context);

            // Do we have X-NuGet-Protocol-Version header?
            var protocolVersion = GetProtocolVersion(context);

            if (protocolVersion == null)
            {
                // Do we have X-NuGet-Client-Version header?
                protocolVersion = GetClientVersion(context);
            }
            
            if (protocolVersion == null || protocolVersion < minClientVersion)
            {
                return SecurityPolicyResult.CreateErrorResult(string.Format(CultureInfo.CurrentCulture,
                    Strings.SecurityPolicy_RequireMinProtocolVersionForPush, minClientVersion));
            }

            return SecurityPolicyResult.SuccessResult;
        }
    }
}