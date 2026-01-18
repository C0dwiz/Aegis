# TODO - Aegis Messenger Protocol

## Фаза 2: Database и остающиеся функции

### Database Integration
- [ ] Добавить регистрацию DbContext в Program.cs
- [ ] Добавить регистрацию всех Repository в DI контейнер
- [ ] Создать миграции Entity Framework Core
- [ ] Добавить инициализацию БД с тестовыми данными
- [ ] Интегрировать UserRepository в AuthHandler

### Функция 11: IPv6 поддержка
- [ ] Изменить TcpServer для поддержки IPv6
- [ ] Добавить DualMode сокеты (IPv4 + IPv6)
- [ ] Обновить конфигурацию ServerOptions
- [ ] Тестировать с IPv6 адресами
- [ ] Документировать в README

### Функция 12: Typing Indicators
- [ ] Создать TypingIndicatorHandler в Aegis.Handlers
- [ ] Добавить MessageType.UserTyping
- [ ] Реализовать хранилище активного набора текста
- [ ] Установить минимальный интервал 500ms между сообщениями
- [ ] Добавить таймер для очистки неактивных индикаторов
- [ ] Написать тесты

### Функция 13: Group Chat - GroupMessageHandler
- [ ] Создать GroupMessageHandler в Aegis.Handlers
- [ ] Добавить MessageType.GroupMessage
- [ ] Реализовать роутинг сообщения всем членам группы
- [ ] Проверить права доступа для отправки
- [ ] Сохранять сообщения в БД
- [ ] Написать тесты

### Функция 14: Group Chat - GroupCreateHandler
- [ ] Создать GroupCreateHandler в Aegis.Handlers
- [ ] Добавить MessageType.GroupCreate
- [ ] Реализовать создание группы в БД
- [ ] Добавить создателя первым членом
- [ ] Отправить подтверждение создателю
- [ ] Написать тесты

### Функция 15: Group Chat - GroupLeaveHandler
- [ ] Создать GroupLeaveHandler в Aegis.Handlers
- [ ] Добавить MessageType.GroupLeave
- [ ] Реализовать удаление пользователя из группы
- [ ] Отправить уведомление остальным членам
- [ ] Удалить группу если нет членов
- [ ] Написать тесты

### Функция 16: Offline Message Queue
- [ ] Создать OfflineMessageService как BackgroundService
- [ ] Сохранять сообщения для оффлайн пользователей в БД
- [ ] Реализовать доставку при подключении пользователя
- [ ] Установить максимум 1000 сообщений на пользователя
- [ ] Удалять старые сообщения при превышении лимита
- [ ] Написать тесты

### Функция 17: Media Support - FileUploadHandler
- [ ] Создать FileUploadHandler в Aegis.Handlers
- [ ] Добавить MessageType.FileTransfer
- [ ] Реализовать прием файлов с разбиением на чанки
- [ ] Сохранять файлы на диск или в облако
- [ ] Возвращать файловый ID для скачивания
- [ ] Установить максимум 100MB на файл
- [ ] Написать тесты

### Функция 18: Media Support - FileDownloadHandler
- [ ] Создать FileDownloadHandler в Aegis.Handlers
- [ ] Реализовать скачивание файлов по ID
- [ ] Отправлять файл с разбиением на чанки
- [ ] Проверять права доступа пользователя
- [ ] Добавить лимит скорости для больших файлов
- [ ] Написать тесты

### Функция 19: Double Ratchet Algorithm
- [ ] Создать DoubleRatchetAlgorithm.cs в Aegis.Crypto
- [ ] Реализовать Signal Protocol версию 3
- [ ] Хранение цепочек ключей в БД
- [ ] Максимум 100 неиспользованных ключей
- [ ] Совместимость с X3DH протоколом
- [ ] Добавить тесты криптографии
- [ ] Интегрировать в MessageHandler

### Функция 20: Load Balancing
- [ ] Создать ConnectionBalancer.cs
- [ ] Реализовать распределение нагрузки между инстансами
- [ ] Connection pooling с эвикцией по возрасту
- [ ] Сбор метрик: CPU, Memory, Connections
- [ ] Graceful connection migration
- [ ] Health check между инстансами
- [ ] Написать тесты

### Функция 21: Integration Tests
- [ ] Создать ServerIntegrationTests.cs в Aegis.Tests
- [ ] Тест: Authentication flow
- [ ] Тест: Message send/receive с acks
- [ ] Тест: Group creation и добавление членов
- [ ] Тест: Offline message delivery
- [ ] Тест: Media upload/download
- [ ] Тест: Connection timeout и reconnection
- [ ] Тест: Rate limiting срабатывание
- [ ] Запустить все тесты и убедиться что проходят

### Функция 22: API Documentation
- [ ] Создать OpenAPI спецификацию
- [ ] Документировать все MessageType'ы
- [ ] Описать структуру каждого сообщения
- [ ] Примеры протокольных обменов
- [ ] Guide по миграции для клиентов
- [ ] Ошибки и коды состояния

### Функция 23: Client SDK
- [ ] Создать Aegis.Client проект
- [ ] Реализовать C# client library
- [ ] Метод Connect для подключения к серверу
- [ ] Метод SendMessage для отправки сообщений
- [ ] Метод RegisterGroup для создания группы
- [ ] Метод UploadFile для загрузки файлов
- [ ] Метод DownloadFile для скачивания
- [ ] Примеры использования
- [ ] Документация для разработчиков

## Дополнительные задачи

### Тестирование
- [ ] Unit тесты для всех новых компонентов
- [ ] Integration тесты для полного flow
- [ ] Performance тесты с нагрузкой
- [ ] Стресс-тесты на 10000+ соединений
- [ ] Тесты безопасности (криптография)
- [ ] Фаззинг тесты для парсера протокола

### Документация
- [ ] Обновить README с новыми возможностями
- [ ] Написать migration guide для v1 -> v2
- [ ] API документация в Swagger
- [ ] Architecture guide для разработчиков
- [ ] Deployment guide
- [ ] Troubleshooting guide

### Performance
- [ ] Профилирование памяти
- [ ] Оптимизация CPU usage
- [ ] Уменьшение latency
- [ ] Benchmarking криптографии
- [ ] Connection pooling оптимизация
- [ ] Message encoding оптимизация

### DevOps
- [ ] Docker контейнеризация
- [ ] Docker Compose для локальной разработки
- [ ] CI/CD pipeline (GitHub Actions)
- [ ] Automated testing в CI/CD
- [ ] Версионирование и릴изы
- [ ] Мониторинг и alerting

### Безопасность
- [ ] Security audit кода
- [ ] Тесты на SQL injection
- [ ] Тесты на buffer overflow
- [ ] Защита от replay attacks
- [ ] Rate limiting усиление
- [ ] Key rotation механизм

---

## Метрики выполнения

Всего задач: 79
Завершено: 0
В работе: 0
Осталось: 79

---

## Заметки

- Database должна быть интегрирована первой, так как она требуется для остальных функций
- IPv6 - простая задача, может быть сделана параллельно
- Group Chat функции зависят друг от друга
- Media Support функции могут быть сделаны параллельно
- Double Ratchet требует глубокого понимания криптографии
- Load Balancing требует архитектурных изменений
- Все функции должны иметь unit тесты
- Integration тесты пишутся после всех компонентов

