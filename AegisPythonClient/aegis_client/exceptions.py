"""
Исключения Aegis Client
"""


class AegisException(Exception):
    """Базовое исключение Aegis"""
    pass


class ConnectionException(AegisException):
    """Исключение при ошибке подключения"""
    pass


class NotConnectedException(AegisException):
    """Исключение при попытке операции без подключения"""
    pass


class TimeoutException(AegisException):
    """Исключение при таймауте операции"""
    pass


class ProtocolError(AegisException):
    """Исключение при ошибке протокола"""
    pass


class AuthenticationException(AegisException):
    """Исключение при ошибке аутентификации"""
    pass


class RegistrationException(AegisException):
    """Исключение при ошибке регистрации"""
    pass
