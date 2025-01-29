// aws-cdk-lib version: 2.x
import { Stack, CfnOutput, Duration, RemovalPolicy } from 'aws-cdk-lib';
// constructs version: 10.x
import { Construct } from 'constructs';
// aws-cdk-lib/aws-cognito version: 2.x
import * as cognito from 'aws-cdk-lib/aws-cognito';
import { 
  UserPool,
  UserPoolClient,
  IdentityPool,
  OAuthScope,
  UserPoolOperation,
  Mfa,
  PasswordPolicy,
  AccountRecovery,
  AdvancedSecurityMode,
  UserPoolEmail,
  VerificationEmailStyle
} from 'aws-cdk-lib/aws-cognito';

// Internal imports
import { NetworkingStack } from './networking-stack';

interface CognitoStackProps {
  environment: string;
  networkingStack: NetworkingStack;
}

export class CognitoStack extends Stack {
  public readonly userPool: UserPool;
  public readonly userPoolClient: UserPoolClient;
  public readonly identityPool: IdentityPool;

  constructor(scope: Construct, id: string, props: CognitoStackProps) {
    super(scope, id);

    // Create the user pool with advanced security features
    this.userPool = this.createUserPool(props);

    // Create the user pool client with OAuth configuration
    this.userPoolClient = this.createUserPoolClient();

    // Create the identity pool with role-based access
    this.identityPool = this.createIdentityPool();

    // Configure VPC endpoints for private access
    this.configureVpcEndpoints(props.networkingStack);

    // Export Cognito resources
    this.exportValues();
  }

  private createUserPool(props: CognitoStackProps): UserPool {
    const userPool = new UserPool(this, 'DocumentsApiUserPool', {
      userPoolName: `estatekit-documents-${props.environment}-user-pool`,
      removalPolicy: RemovalPolicy.RETAIN,
      
      // Advanced security features
      advancedSecurityMode: AdvancedSecurityMode.ENFORCED,
      
      // MFA Configuration
      mfa: Mfa.REQUIRED,
      mfaSecondFactor: {
        sms: true,
        otp: true
      },
      
      // Password policy
      passwordPolicy: {
        minLength: 12,
        requireLowercase: true,
        requireUppercase: true,
        requireDigits: true,
        requireSymbols: true,
        tempPasswordValidity: Duration.days(3)
      },
      
      // Account recovery
      accountRecovery: AccountRecovery.EMAIL_AND_PHONE_NUMBER,
      
      // Email configuration
      email: UserPoolEmail.withCognito(),
      emailSettings: {
        from: 'no-reply@estatekit.com',
        replyTo: 'support@estatekit.com'
      },
      
      // User verification
      selfSignUpEnabled: false,
      userVerification: {
        emailStyle: VerificationEmailStyle.CODE,
        emailSubject: 'EstateKit Documents API - Verify your email',
        emailBody: 'Your verification code is {####}',
        smsMessage: 'Your EstateKit verification code is {####}'
      },
      
      // Required attributes
      standardAttributes: {
        email: {
          required: true,
          mutable: false
        },
        phoneNumber: {
          required: true,
          mutable: true
        }
      },
      
      // Custom attributes
      customAttributes: {
        tenantId: new cognito.StringAttribute({ mutable: false }),
        role: new cognito.StringAttribute({ mutable: true }),
        lastLogin: new cognito.DateTimeAttribute()
      },
      
      // Device tracking
      deviceTracking: {
        challengeRequiredOnNewDevice: true,
        deviceOnlyRememberedOnUserPrompt: true
      }
    });

    // Add domain
    userPool.addDomain('DocumentsApiDomain', {
      cognitoDomain: {
        domainPrefix: `estatekit-documents-${props.environment}`
      }
    });

    return userPool;
  }

  private createUserPoolClient(): UserPoolClient {
    return new UserPoolClient(this, 'DocumentsApiClient', {
      userPool: this.userPool,
      
      // OAuth configuration
      oAuth: {
        flows: {
          authorizationCodeGrant: true,
          implicitCodeGrant: false
        },
        scopes: [
          OAuthScope.EMAIL,
          OAuthScope.PHONE,
          OAuthScope.OPENID,
          OAuthScope.PROFILE,
          OAuthScope.custom('documents/read'),
          OAuthScope.custom('documents/write')
        ],
        callbackUrls: [
          'https://api.estatekit.com/auth/callback',
          'https://documents.estatekit.com/auth/callback'
        ],
        logoutUrls: [
          'https://api.estatekit.com/auth/logout',
          'https://documents.estatekit.com/auth/logout'
        ]
      },
      
      // Token configuration
      accessTokenValidity: Duration.minutes(60),
      idTokenValidity: Duration.minutes(60),
      refreshTokenValidity: Duration.days(30),
      
      // Security features
      preventUserExistenceErrors: true,
      enableTokenRevocation: true,
      generateSecret: true,
      
      // Auth flows
      authFlows: {
        adminUserPassword: true,
        custom: true,
        userPassword: true,
        userSrp: true
      },
      
      // Supported identity providers
      supportedIdentityProviders: [
        cognito.UserPoolClientIdentityProvider.COGNITO
      ]
    });
  }

  private createIdentityPool(): IdentityPool {
    return new IdentityPool(this, 'DocumentsApiIdentityPool', {
      identityPoolName: 'EstateKitDocumentsIdentityPool',
      
      // Authentication providers
      authenticationProviders: {
        userPools: [{
          userPool: this.userPool,
          userPoolClient: this.userPoolClient
        }]
      },
      
      // Allow unauthenticated access (disabled for security)
      allowUnauthenticatedIdentities: false
    });
  }

  private configureVpcEndpoints(networkingStack: NetworkingStack): void {
    // Cognito endpoints are created in the networking stack
    // This ensures secure private access to Cognito services
  }

  private exportValues(): void {
    // Export Cognito resource ARNs and configurations
    new CfnOutput(this, 'UserPoolId', {
      value: this.userPool.userPoolId,
      description: 'Cognito User Pool ID'
    });

    new CfnOutput(this, 'UserPoolClientId', {
      value: this.userPoolClient.userPoolClientId,
      description: 'Cognito User Pool Client ID'
    });

    new CfnOutput(this, 'IdentityPoolId', {
      value: this.identityPool.identityPoolId,
      description: 'Cognito Identity Pool ID'
    });

    new CfnOutput(this, 'UserPoolDomain', {
      value: `${this.userPool.userPoolProviderName}.auth.${this.region}.amazoncognito.com`,
      description: 'Cognito User Pool Domain'
    });
  }
}