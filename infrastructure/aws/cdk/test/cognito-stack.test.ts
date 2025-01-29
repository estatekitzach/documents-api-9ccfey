import { App } from 'aws-cdk-lib';
import { Template, Match } from 'aws-cdk-lib/assertions';
import { CognitoStack } from '../lib/cognito-stack';
import { NetworkingStack } from '../lib/networking-stack';

let testApp: App;
let testStack: CognitoStack;
let networkingStack: NetworkingStack;
let template: Template;

describe('CognitoStack', () => {
  beforeEach(() => {
    testApp = new App();
    networkingStack = new NetworkingStack(testApp, 'TestNetworkingStack', {
      environment: 'test',
      vpcCidr: '10.0.0.0/16',
      maxAzs: 2
    });
    testStack = new CognitoStack(testApp, 'TestCognitoStack', {
      environment: 'test',
      networkingStack: networkingStack
    });
    template = Template.fromStack(testStack);
  });

  test('userPool creation and configuration', () => {
    // Verify UserPool resource creation
    template.hasResourceProperties('AWS::Cognito::UserPool', {
      UserPoolName: Match.stringLikeRegexp('estatekit-documents-test-user-pool'),
      
      // Verify MFA configuration
      MfaConfiguration: 'REQUIRED',
      EnabledMfaTypes: Match.arrayWith(['SMS_MFA', 'SOFTWARE_TOKEN_MFA']),
      
      // Verify password policy
      Policies: {
        PasswordPolicy: {
          MinimumLength: 12,
          RequireLowercase: true,
          RequireUppercase: true,
          RequireNumbers: true,
          RequireSymbols: true,
          TemporaryPasswordValidityDays: 3
        }
      },
      
      // Verify email configuration
      EmailConfiguration: {
        EmailSendingAccount: 'COGNITO_DEFAULT',
        From: 'no-reply@estatekit.com',
        ReplyToEmailAddress: 'support@estatekit.com'
      },
      
      // Verify user verification settings
      AdminCreateUserConfig: {
        AllowAdminCreateUserOnly: true
      },
      
      // Verify advanced security features
      UserPoolAddOns: {
        AdvancedSecurityMode: 'ENFORCED'
      },
      
      // Verify schema attributes
      Schema: Match.arrayWith([
        Match.objectLike({
          Name: 'email',
          Required: true,
          Mutable: false
        }),
        Match.objectLike({
          Name: 'phone_number',
          Required: true,
          Mutable: true
        }),
        Match.objectLike({
          Name: 'tenantId',
          AttributeDataType: 'String',
          Mutable: false
        })
      ])
    });

    // Verify domain configuration
    template.hasResourceProperties('AWS::Cognito::UserPoolDomain', {
      Domain: Match.stringLikeRegexp('estatekit-documents-test'),
      UserPoolId: Match.anyValue()
    });
  });

  test('userPoolClient OAuth configuration', () => {
    template.hasResourceProperties('AWS::Cognito::UserPoolClient', {
      UserPoolId: Match.anyValue(),
      
      // Verify OAuth flows
      AllowedOAuthFlows: ['code'],
      AllowedOAuthFlowsUserPoolClient: true,
      AllowedOAuthScopes: Match.arrayWith([
        'email',
        'phone',
        'openid',
        'profile',
        'documents/read',
        'documents/write'
      ]),
      
      // Verify callback URLs
      CallbackURLs: Match.arrayWith([
        'https://api.estatekit.com/auth/callback',
        'https://documents.estatekit.com/auth/callback'
      ]),
      LogoutURLs: Match.arrayWith([
        'https://api.estatekit.com/auth/logout',
        'https://documents.estatekit.com/auth/logout'
      ]),
      
      // Verify token settings
      AccessTokenValidity: 60,
      IdTokenValidity: 60,
      RefreshTokenValidity: 30,
      
      // Verify security settings
      PreventUserExistenceErrors: 'ENABLED',
      EnableTokenRevocation: true,
      GenerateSecret: true,
      
      // Verify auth flows
      ExplicitAuthFlows: Match.arrayWith([
        'ADMIN_USER_PASSWORD_AUTH',
        'CUSTOM_AUTH_FLOW_ONLY',
        'USER_PASSWORD_AUTH',
        'USER_SRP_AUTH'
      ]),
      
      // Verify supported identity providers
      SupportedIdentityProviders: ['COGNITO']
    });
  });

  test('identityPool setup and roles', () => {
    template.hasResourceProperties('AWS::Cognito::IdentityPool', {
      IdentityPoolName: 'EstateKitDocumentsIdentityPool',
      
      // Verify authentication providers
      CognitoIdentityProviders: Match.arrayWith([
        Match.objectLike({
          ClientId: Match.anyValue(),
          ProviderName: Match.anyValue(),
          ServerSideTokenCheck: true
        })
      ]),
      
      // Verify unauthenticated access
      AllowUnauthenticatedIdentities: false
    });

    // Verify IAM roles
    template.hasResourceProperties('AWS::IAM::Role', {
      AssumeRolePolicyDocument: Match.objectLike({
        Statement: Match.arrayWith([
          Match.objectLike({
            Effect: 'Allow',
            Principal: {
              Federated: 'cognito-identity.amazonaws.com'
            }
          })
        ])
      })
    });
  });

  test('stack outputs', () => {
    template.hasOutput('UserPoolId', {});
    template.hasOutput('UserPoolClientId', {});
    template.hasOutput('IdentityPoolId', {});
    template.hasOutput('UserPoolDomain', {});
  });

  test('resource protection', () => {
    template.hasResource('AWS::Cognito::UserPool', {
      DeletionPolicy: 'Retain',
      UpdateReplacePolicy: 'Retain'
    });
  });

  test('custom domain configuration', () => {
    template.hasResourceProperties('AWS::Cognito::UserPoolDomain', {
      Domain: Match.stringLikeRegexp('estatekit-documents-test'),
      UserPoolId: Match.anyValue()
    });
  });
});