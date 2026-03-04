# Test Configuration for Aegis Messenger

This directory contains comprehensive tests for the Aegis Messenger server, covering all new functionality including:

## Test Categories

### 1. **UserServicesTests.cs**
Tests for user-related services:
- User registration with validation
- Authentication and session management
- User search functionality
- Error handling for invalid inputs

### 2. **NewHandlerTests.cs**
Tests for new message handlers:
- Registration handler
- User search handler
- Channel message handler
- Channel creation handler
- Private chat message handler

### 3. **RepositoryTests.cs**
Tests for data repositories:
- User repository operations
- Channel repository operations
- Private chat repository operations
- Database constraint validation

### 4. **NewProtocolTests.cs**
Tests for protocol message types:
- Serialization/deserialization of new message types
- Message type validation
- Payload structure verification

### 5. **IntegrationTests.cs**
End-to-end integration tests:
- Complete user registration and authentication flow
- Channel creation and management
- Private chat functionality
- Session management
- Database constraint enforcement

### 6. **Updated HandlerTests.cs**
Enhanced existing handler tests:
- Message routing for new message types
- Handler type validation
- Unknown message type handling

## Running Tests

### Prerequisites
- .NET 10.0 SDK
- Test dependencies (configured in project file)

### Run All Tests
```bash
cd tests/Aegis.Tests
dotnet test
```

### Run Specific Test Category
```bash
# Run user service tests
dotnet test --filter "FullyQualifiedName~UserServicesTests"

# Run handler tests
dotnet test --filter "FullyQualifiedName~NewHandlerTests"

# Run integration tests
dotnet test --filter "FullyQualifiedName~IntegrationTests"
```

### Run with Coverage
```bash
dotnet test --collect:"XPlat Code Coverage"
```

## Test Features

### Mocking Strategy
- **Moq** for service dependencies
- **In-memory database** for repository tests
- **Test doubles** for external dependencies

### Test Data
- Unique database names for isolation
- Realistic test data scenarios
- Edge case coverage

### Assertions
- Comprehensive validation of business logic
- Error condition testing
- Database constraint verification

## Coverage Areas

### ✅ User Management
- Registration with validation
- Authentication and sessions
- Search functionality
- Password hashing

### ✅ Channel Management
- Channel creation
- Member management
- Permission handling
- Message routing

### ✅ Private Chats
- Chat creation
- Message exchange
- User pairing

### ✅ Protocol Layer
- Message serialization
- Type validation
- Payload integrity

### ✅ Data Layer
- Repository operations
- Database constraints
- Entity relationships

### ✅ Integration
- End-to-end flows
- Service coordination
- Error propagation

## Test Best Practices

1. **Isolation**: Each test uses a unique in-memory database
2. **Arrange-Act-Assert**: Clear test structure
3. **Descriptive Names**: Test names describe the scenario
4. **Comprehensive Coverage**: Happy path and error cases
5. **Mock Verification**: Verify service interactions
6. **Cleanup**: Proper resource disposal

## Continuous Integration

These tests are designed to run in CI/CD pipelines:
- Fast execution with in-memory database
- No external dependencies
- Deterministic results
- Clear failure reporting

## Adding New Tests

When adding new functionality:

1. **Unit Tests**: Test individual components in isolation
2. **Integration Tests**: Test component interactions
3. **Protocol Tests**: Verify message serialization
4. **Repository Tests**: Test data access layer
5. **Handler Tests**: Test message processing

Follow the existing patterns and naming conventions for consistency.
