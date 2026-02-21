# Aegis Server Docker

Docker конфигурация для запуска Aegis Messenger Server с получением IP и порта для подключения.

## 🚀 Быстрый запуск

### Способ 1: Makefile

```bash
make run
```

### Способ 2: Docker Compose

```bash
docker-compose -f docker-compose.yaml up --build
```

### Способ 3: Ручной запуск

```bash
# Сборка образа
docker build -f Dockerfile.simple -t aegis-server ../git/Aegis

# Запуск контейнера
docker run -d --name aegis-server -p 8888:8888 aegis-server

# Проверка статуса
docker ps
```

## 📋 Полезные команды

### Управление сервером

```bash
# Остановить сервер
make stop

# Посмотреть логи
make logs

# Проверить статус
make status

# Тестировать соединение
make test

# Очистить всё
make clean
```

### Docker команды

```bash
# Посмотреть логи в реальном времени
docker logs aegis-server -f

# Зайти в контейнер
docker exec -it aegis-server /bin/bash

# Перезапустить сервер
docker restart aegis-server

# Посмотреть IP адрес контейнера
docker inspect -f '{{range .NetworkSettings.Networks}}{{.IPAddress}}{{end}}' aegis-server
```

## 🔗 Подключение клиента

После запуска сервер будет доступен по:

- **Localhost**: `localhost:8888`
- **Container IP**: `[IP-адрес]:8888`

### Dart клиент пример

```dart
import 'package:aegis_client/aegis_client.dart';

void main() async {
  final client = AegisClient();
  
  try {
    // Подключение к серверу
    await client.connect('localhost', 8888);
    print('Connected to Aegis server!');
    
    // Аутентификация
    await client.authenticate('your_auth_token');
    print('Authenticated!');
    
    // Отправка сообщения
    await client.sendMessage('Hello from Docker client!');
    print('Message sent!');
    
    // Прослушивание входящих сообщений
    client.messages.listen((message) {
      print('Received: ${message.type}');
    });
    
  } catch (e) {
    print('Error: $e');
  }
}
```

## 🌐 Сетевые настройки

### Порты

- **8888** - Основной порт Aegis протокола
- **8080** - Health check endpoint (опционально)

### Docker Compose сервисы

- **aegis-server** - Основной сервер
- **postgres** - База данных (опционально)
- **redis** - Кэш сессий (опционально)

## 📊 Мониторинг

### Health check

```bash
# Проверить здоровье контейнера
docker inspect --format='{{.State.Health.Status}}' aegis-server

# Посмотреть статистику
docker stats aegis-server
```

### Логи

```bash
# Последние 20 строк
docker logs aegis-server --tail 20

# Логи с временными метками
docker logs aegis-server --timestamps

# Фильтрация по уровню
docker logs aegis-server | grep ERROR
```

## 🔧 Конфигурация

### Переменные окружения

```bash
# Порт сервера
AEGIS_PORT=8888

# Максимальное количество соединений
AEGIS_MAX_CONNECTIONS=10000

# Размер буфера
AEGIS_BUFFER_SIZE=8192

# Уровень логирования
Logging__LogLevel=Information
```

### Изменение порта

```bash
# Изменить порт в docker-compose.yaml
ports:
  - "9999:8888"  # Внешний:внутренний

# Или при запуске
docker run -p 9999:8888 aegis-server
```

## 🐛 Troubleshooting

### Проблема: Сервер не запускается

```bash
# Проверить логи
docker logs aegis-server

# Проверить образ
docker images aegis-server

# Пересобрать
make clean && make build
```

### Проблема: Нет подключения

```bash
# Проверить открытые порты
docker port aegis-server

# Проверить сетевые настройки
docker network ls
docker network inspect bridge
```

### Проблема: Контейнер падает

```bash
# Проверить использование ресурсов
docker stats aegis-server

# Проверить диск
df -h

# Проверить память
free -h
```

## 📦 Production развертывание

### Docker Compose production

```bash
# Создать production конфиг
cp docker-compose.yaml docker-compose.prod.yaml

# Изменить настройки:
# - Добавить volume для логов
# - Настроить restart policy
# - Добавить external network
# - Настроить secrets
```

### Пример production конфига

```yaml
version: '3.8'
services:
  aegis-server:
    image: aegis-server:latest
    restart: always
    ports:
      - "8888:8888"
    volumes:
      - ./logs:/app/logs
      - ./data:/app/data
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Logging__LogLevel=Warning
    deploy:
      resources:
        limits:
          memory: 512M
          cpus: '0.5'
```

## 🔒 Безопасность

### Рекомендации

1. **Не использовать root пользователя**
2. **Ограничить ресурсы**
3. **Использовать HTTPS для внешних подключений**
4. **Настроить firewall**
5. **Регулярно обновлять образы**

### Пример с ограничениями

```yaml
deploy:
  resources:
    limits:
      memory: 512M
      cpus: '0.5'
  security_opt:
    - no-new-privileges:true
  read_only: true
  tmpfs:
    - /tmp
```

## 📞 Поддержка

- GitHub Issues: https://github.com/C0dwiz/Aegis/issues
- Документация: [ссылка на документацию]
