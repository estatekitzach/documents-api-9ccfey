// ts-jest version: ^29.0.0
// jest version: ^29.0.0

module.exports = {
  // Use Node.js as the test environment
  testEnvironment: 'node',

  // Define test file locations
  roots: ['<rootDir>/test'],

  // Match TypeScript test files
  testMatch: ['**/*.test.ts'],

  // Configure TypeScript preprocessing with ts-jest
  transform: {
    '^.+\\.tsx?$': 'ts-jest'
  },

  // Enable code coverage collection
  collectCoverage: true,
  coverageDirectory: 'coverage',

  // Set minimum coverage thresholds as per requirements
  coverageThreshold: {
    global: {
      branches: 80,
      functions: 80,
      lines: 80,
      statements: 80
    }
  },

  // Set test timeout to 30 seconds for AWS operations
  testTimeout: 30000,

  // Enable verbose test output
  verbose: true
};