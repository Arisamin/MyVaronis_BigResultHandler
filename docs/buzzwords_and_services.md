# Essential Buzzwords, Services, and Technologies for System Design & Architecture Interviews

This document provides concise definitions and roles for common buzzwords, services, and technologies you may encounter in technical interviews. Use it as a quick reference to avoid surprises and to understand the high-level purpose of each term.

---

## Cloud & Infrastructure

- **AWS / Azure / GCP**: Major cloud providers offering compute, storage, networking, and managed services for scalable applications.
- **Load Balancer**: Distributes incoming network traffic across multiple servers to ensure reliability and performance.
- **CDN (Content Delivery Network)**: Caches and delivers static content from edge locations closer to users for faster access.
- **VPC (Virtual Private Cloud)**: Isolated network environment in the cloud for secure resource deployment.
- **Kubernetes**: Container orchestration platform for automating deployment, scaling, and management of containerized applications.
- **Docker**: Platform for packaging applications and dependencies into portable containers.
- **Serverless (e.g., AWS Lambda, Azure Functions)**: Event-driven compute model where the cloud provider manages server infrastructure.


## Data & Storage

- **RDBMS (e.g., SQL Server, PostgreSQL, MySQL)**: Relational databases for structured data and ACID transactions.
- **NoSQL (e.g., MongoDB, Cassandra, DynamoDB)**: Non-relational databases for flexible, scalable storage (document, key-value, wide-column, graph).
- **Data Lake**: Centralized repository for storing raw, unstructured, and structured data at scale.
- **Data Warehouse**: Optimized for analytical queries and reporting on large datasets (e.g., Snowflake, Redshift, BigQuery).
- **Cache (e.g., Redis, Memcached)**: In-memory data store for fast access to frequently used data.
- **Object Storage (e.g., S3, Blob Storage)**: Scalable storage for unstructured data (files, images, backups).
- **Elasticsearch**: Distributed search and analytics engine for fast, scalable full-text search, log analysis, and data exploration. Often used as the search backend for applications and as part of the ELK stack (Elasticsearch, Logstash, Kibana).

## Messaging & Integration

- **Message Queue (e.g., RabbitMQ, SQS, Kafka)**: Decouples producers and consumers, enabling asynchronous communication and buffering.
- **Event Bus / Event Streaming (e.g., Kafka, EventBridge)**: Publishes and subscribes to streams of events for real-time processing.
- **API Gateway**: Entry point for APIs, handling routing, security, rate limiting, and protocol translation.
- **Service Mesh (e.g., Istio, Linkerd)**: Manages service-to-service communication, security, and observability in microservices.

## Application Architecture

- **Microservices**: Architectural style where applications are composed of small, independent services.
- **Monolith**: Single, unified codebase/application.
- **SOA (Service-Oriented Architecture)**: Organizes software as reusable services, often with an enterprise service bus.
- **CQRS (Command Query Responsibility Segregation)**: Separates read and write operations for scalability and maintainability.
- **Event Sourcing**: Stores state changes as a sequence of events, enabling auditability and replay.
- **REST / gRPC / GraphQL**: Protocols for client-server communication (REST: HTTP/JSON, gRPC: binary/protobuf, GraphQL: flexible queries).

## Security & Observability

- **OAuth / OpenID Connect**: Protocols for authentication and authorization.
- **JWT (JSON Web Token)**: Compact, self-contained token for securely transmitting information.
- **IAM (Identity and Access Management)**: Manages user identities and permissions.
- **SIEM (Security Information and Event Management)**: Aggregates and analyzes security data for threat detection.
- **Tracing (e.g., OpenTelemetry, Jaeger)**: Tracks requests across distributed systems for debugging and performance analysis.
- **Metrics & Monitoring (e.g., Prometheus, Grafana, CloudWatch)**: Collects and visualizes system health and performance data.
- **Logging (e.g., ELK Stack, Splunk)**: Centralized collection and analysis of application logs.



## AI & Machine Learning ([See: Catching up with AI IDE](./Catching%20up%20with%20AI%20IDE))

- **Model**: A mathematical representation or algorithm trained to recognize patterns or make predictions from data.
- **Training**: The process of teaching a model using data so it can make accurate predictions or classifications.
- **Inference**: Using a trained model to make predictions on new, unseen data.
- **Supervised Learning**: Machine learning where models are trained on labeled data (input-output pairs).
- **Unsupervised Learning**: Machine learning where models find patterns or groupings in unlabeled data.
- **Reinforcement Learning**: Training models through trial and error, receiving rewards or penalties for actions.
- **LLM (Large Language Model)**: A type of AI model (e.g., GPT, Claude) trained on vast text data to understand and generate human language.
- **Claude**: A family of large language models developed by Anthropic, used for natural language understanding and generation.
- **GPT (Generative Pre-trained Transformer)**: A family of large language models developed by OpenAI.
- **RAG (Retrieval-Augmented Generation)**: Combines information retrieval with generative models to provide more accurate and up-to-date responses.
- **MCP (Model Context Protocol)**: A protocol or framework for managing model context, often used to coordinate between models and external data sources.
- **Prompt Engineering**: Crafting input prompts to guide the behavior and output of language models.
- **Fine-tuning**: Further training a pre-trained model on a specific dataset to specialize it for a particular task.
- **Embedding**: A numerical representation of data (text, images, etc.) in a vector space, used for similarity and search.
- **Feature Store**: Centralized repository for storing, sharing, and managing features used in machine learning models.
- **MLOps**: Practices and tools for managing the lifecycle of machine learning models, including deployment, monitoring, and governance.

## DevOps & CI/CD

- **CI/CD (Continuous Integration/Continuous Deployment)**: Automates code integration, testing, and deployment.
- **IaC (Infrastructure as Code, e.g., Terraform, CloudFormation)**: Manages infrastructure using code for repeatability and versioning.
- **Artifact Repository (e.g., Artifactory, Nexus)**: Stores build artifacts (binaries, containers) for deployment.

## Other Key Terms

- **Sharding**: Splitting data across multiple databases or tables for scalability.
- **Replication**: Copying data across multiple nodes for redundancy and availability.
- **Failover**: Automatic switching to a backup system in case of failure.
- **CAP Theorem**: Consistency, Availability, Partition Tolerance—trade-offs in distributed systems.
- **Idempotency**: Operation can be applied multiple times without changing the result beyond the initial application.
- **Backpressure**: Mechanism to prevent overwhelming a system by controlling the flow of data.
- **Blue/Green Deployment**: Deploying new versions alongside old ones for safe cutover.
- **Canary Release**: Gradually rolling out changes to a subset of users to minimize risk.

---

*This list is not exhaustive but covers the most common terms and technologies you may encounter in interviews. For each, know the basic definition, what problem it solves, and where it fits in a typical system architecture.*
