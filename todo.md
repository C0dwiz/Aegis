# TODO - Aegis Messenger Protocol

## Фаза 2: Database и остающиеся функции

### Database Integration

- [x] Добавить регистрацию DbContext в Program.cs
- [x] Добавить регистрацию всех Repository в DI контейнер
- [x] Создать миграции Entity Framework Core
- [ ] Добавить инициализацию БД с тестовыми данными
- [x] Интегрировать UserRepository в AuthHandler

### Функция 11: IPv6 поддержка

- [x] Изменить TcpServer для поддержки IPv6
- [x] Добавить DualMode сокеты (IPv4 + IPv6)
- [x] Обновить конфигурацию ServerOptions
- [ ] Тестировать с IPv6 адресами
- [ ] Документировать в README

### Функция 12: Typing Indicators

- [x] Создать TypingIndicatorHandler в Aegis.Handlers
- [x] Добавить MessageType.UserTyping
- [x] Реализовать хранилище активного набора текста
- [x] Установить минимальный интервал 500ms между сообщениями
- [x] Добавить таймер для очистки неактивных индикаторов
- [ ] Написать тесты

### Функция 13: Group Chat - GroupMessageHandler

- [x] Создать GroupMessageHandler в Aegis.Handlers
- [x] Добавить MessageType.GroupMessage
- [x] Реализовать роутинг сообщения всем членам группы
- [x] Проверить права доступа для отправки
- [x] Сохранять сообщения в БД
- [ ] Написать тесты

### Функция 14: Group Chat - GroupCreateHandler

- [x] Создать GroupCreateHandler в Aegis.Handlers
- [x] Добавить MessageType.GroupCreate
- [x] Реализовать создание группы в БД
- [x] Добавить создателя первым членом
- [x] Отправить подтверждение создателю
- [ ] Написать тесты

### Функция 15: Group Chat - GroupLeaveHandler

- [x] Создать GroupLeaveHandler в Aegis.Handlers
- [x] Добавить MessageType.GroupLeave
- [x] Реализовать удаление пользователя из группы
- [x] Отправить уведомление остальным членам
- [x] Удалить группу если нет членов
- [ ] Написать тесты

### Функция 16: Offline Message Queue

- [x] Создать OfflineMessageService как BackgroundService
- [x] Сохранять сообщения для оффлайн пользователей в БД
- [x] Реализовать доставку при подключении пользователя
- [x] Установить максимум 1000 сообщений на пользователя
- [x] Удалять старые сообщения при превышении лимита
- [ ] Написать тесты

### Функция 17: Media Support - FileUploadHandler

- [x] Создать FileUploadHandler в Aegis.Handlers
- [x] Добавить MessageType.FileTransfer
- [x] Реализовать прием файлов с разбиением на чанки
- [x] Сохранять файлы на диск или в облако
- [x] Возвращать файловый ID для скачивания
- [x] Установить максимум 100MB на файл
- [ ] Написать тесты

### Функция 18: Media Support - FileDownloadHandler

- [x] Создать FileDownloadHandler в Aegis.Handlers
- [x] Реализовать скачивание файлов по ID
- [x] Отправлять файл с разбиением на чанки
- [x] Проверять права доступа пользователя
- [x] Добавить лимит скорости для больших файлов
- [x] Написать тесты

### Функция 19: Double Ratchet Algorithm

- [x] Создать DoubleRatchetAlgorithm.cs в Aegis.Crypto
- [ ] Реализовать Signal Protocol версию 3
- [x] Хранение цепочек ключей в БД
- [x] Максимум 100 неиспользованных ключей
- [ ] Совместимость с X3DH протоколом
- [ ] Добавить тесты криптографии
- [x] Интегрировать в MessageHandler

### Функция 20: Load Balancing

- [x] Создать ConnectionBalancer.cs
- [x] Реализовать распределение нагрузки между инстансами
- [x] Connection pooling с эвикцией по возрасту
- [x] Сбор метрик: CPU, Memory, Connections
- [x] Graceful connection migration
- [x] Health check между инстансами
- [x] Написать тесты

### Функция 21: Integration Tests

- [x] Создать ServerIntegrationTests.cs в Aegis.Tests
- [x] Тест: Authentication flow
- [x] Тест: Message send/receive с acks
- [x] Тест: Group creation и добавление членов
- [x] Тест: Offline message delivery
- [x] Тест: Media upload/download
- [x] Тест: Connection timeout и reconnection
- [x] Тест: Rate limiting срабатывание
- [x] Запустить все тесты и убедиться что проходят

### Функция 22: API Documentation

- [x] Создать OpenAPI спецификацию
- [x] Документировать все MessageType'ы
- [x] Описать структуру каждого сообщения
- [x] Примеры протокольных обменов
- [x] Guide по миграции для клиентов
- [x] Ошибки и коды состояния

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
- [x] Фаззинг тесты для парсера протокола

### Документация

- [x] Обновить README с новыми возможностями
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

- [x] Docker контейнеризация
- [x] Docker Compose для локальной разработки
- [x] CI/CD pipeline (GitHub Actions)
- [x] Automated testing в CI/CD
- [ ] Версионирование и릴изы
- [ ] Мониторинг и alerting

### Безопасность

- [ ] Security audit кода
- [ ] Тесты на SQL injection
- [ ] Тесты на buffer overflow
- [x] Защита от replay attacks
- [ ] Rate limiting усиление
- [ ] Key rotation механизм

---

## Метрики выполнения

Всего задач: 79
Завершено: 27
В работе: 0
Осталось: 52

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
