﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using ArchPM.FluentRabbitMQ.Configs;
using ArchPM.FluentRabbitMQ.Configs.Infos;
using ArchPM.FluentRabbitMQ.Exceptions;
using ArchPM.NetCore.Extensions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ArchPM.FluentRabbitMQ
{
    /// <summary>
    /// 
    /// </summary>
    /// <seealso cref="ArchPM.FluentRabbitMQ.IFluentRabbit" />
    /// <seealso cref="System.IDisposable" />
    // ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
    public class FluentRabbit : IDisposable, IFluentRabbit
    {
        /// <summary>
        /// The instance
        /// </summary>
        public static readonly FluentRabbit Instance = new FluentRabbit();

        /// <summary>
        /// Occurs when [trace occured].
        /// </summary>
        protected event EventHandler<TraceData> TraceOccured = delegate { };

        /// <summary>
        /// Fires the trace occured.
        /// </summary>
        /// <param name="methodBase">The method base.</param>
        /// <param name="message">The message.</param>
        protected virtual void FireTraceOccured(MethodBase methodBase, string message)
        {
            TraceOccured(this, new TraceData()
            {
                Method = methodBase,
                Message = message,
            });
        }
        /// <summary>
        /// Fires the trace occured.
        /// </summary>
        /// <param name="methodBase">The method base.</param>
        /// <param name="ex">The ex.</param>
        protected virtual void FireTraceOccured(MethodBase methodBase, Exception ex)
        {
            TraceOccured(this, new TraceData()
            {
                Method = methodBase,
                Message = ex.Message,
                Exception = ex
            });
        }

        /// <summary>
        /// Tries the catch trace.
        /// </summary>
        /// <param name="methodBase">The method base.</param>
        /// <param name="action">The action.</param>
        /// <returns></returns>
        protected virtual IFluentRabbit TryCatch_Trace(MethodBase methodBase, Action action)
        {
            try
            {
                //trace
                FireTraceOccured(methodBase, "calling...");

                //execute
                action();

                //trace
                FireTraceOccured(methodBase, "called.");
            }
            catch (Exception ex)
            {
                FireTraceOccured(methodBase, ex);
                throw;
            }

            return this;
        }


        /// <summary>
        /// Gets the configuration.
        /// </summary>
        /// <value>
        /// The configuration.
        /// </value>
        public FluentRabbitConfiguration Configuration { get; private set; } = new FluentRabbitConfiguration();

        /// <summary>
        /// Gets the rabbit mq client.
        /// </summary>
        /// <value>
        /// The rabbit mq client.
        /// </value>
        public RabbitMqClient RabbitMqClient { get; } = new RabbitMqClient();

        /// <summary>
        /// Traces the specified trace action. this must be called first in the order.
        /// </summary>
        /// <param name="traceAction">The trace action.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Trace(Action<TraceData> traceAction)
        {
            TraceOccured += (s, t) => { traceAction?.Invoke(t); };

            return this;
        }


        /// <summary>
        /// Configures the specified configuration action.
        /// </summary>
        /// <param name="configAction">The configuration action.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Configure(Action<FluentRabbitConfiguration> configAction = null)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    configAction?.Invoke(Configuration);
                });
        }

        /// <summary>
        /// Configures the specified configuration.
        /// </summary>
        /// <param name="config">The configuration.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Configure(FluentRabbitConfiguration config)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    //validate
                    config.ThrowExceptionIfNull<ArgumentNullException>(nameof(config));

                    //execute
                    Configuration = config;

                });

        }

        /// <summary>
        /// Configures up.
        /// </summary>
        /// <param name="configAction">The configuration action.</param>
        /// <returns></returns>
        public virtual IFluentRabbit ConfigureUp(Action<FluentRabbitConfiguration> configAction = null)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    //execute
                    configAction?.Invoke(Configuration);

                    foreach (var exchange in Configuration.Exchanges)
                    {
                        CreateExchange(exchange.Name, exchange.Config);
                    }
                    foreach (var queue in Configuration.Queues)
                    {
                        CreateQueue(queue.Name, queue.Config);
                    }
                    foreach (var binding in Configuration.Bindings)
                    {
                        Bind(binding.Config);
                    }

                });
        }

        /// <summary>
        /// Configures down.
        /// </summary>
        /// <returns></returns>
        public virtual IFluentRabbit ConfigureDown()
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    foreach (var exchange in Configuration.Exchanges)
                    {
                        DeleteExchange(
                            exchange.Name,
                            new DeleteExchangeConfig() { IfUnused = false }
                        );
                    }
                    foreach (var queue in Configuration.Queues)
                    {
                        DeleteQueue(
                            queue.Name,
                            new DeleteQueueConfig()
                            { IfUnused = false, IfEmpty = false }
                        );
                    }
                });

        }


        /// <summary>
        /// Connects this instance.
        /// </summary>
        /// <returns></returns>
        public virtual IFluentRabbit Connect()
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    RabbitMqClient.ConnectionFactory = new ConnectionFactory()
                    {
                        HostName = Configuration.Connection.Host,
                        Password = Configuration.Connection.Password,
                        Port = Configuration.Connection.Port,
                        UserName = Configuration.Connection.Username,
                        VirtualHost = Configuration.Connection.VirtualHost
                    };

                    RabbitMqClient.Connection = RabbitMqClient.ConnectionFactory.CreateConnection();
                    RabbitMqClient.Model = RabbitMqClient.Connection.CreateModel();
                });

        }

        

        #region Create Exchange
        /// <summary>
        /// Creates the exchange.
        /// </summary>
        /// <param name="exchangeName">Name of the exchange.</param>
        /// <param name="config">The configuration.</param>
        /// <returns></returns>
        public IFluentRabbit CreateExchange(string exchangeName, CreateExchangeConfig config)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
            () =>
                {
                    //validation
                    config.ThrowExceptionIfNull<ArgumentNullException>(nameof(config));
                    RabbitMqClient.Model.ThrowExceptionIfNull<ModelIsNullException>();

                    //execution
                    RabbitMqClient.Model.ExchangeDeclare(exchangeName, config.Type, config.Durable, config.AutoDelete, config.Arguments);
                });
        }

        /// <summary>
        /// Creates the exchange.
        /// </summary>
        /// <param name="exchangeName">Name of the exchange.</param>
        /// <param name="configAction">The configuration action.</param>
        /// <returns></returns>
        public virtual IFluentRabbit CreateExchange(string exchangeName, Action<CreateExchangeConfig> configAction = null)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    var exchangeInfo = Configuration.Exchanges.FirstOrDefault(p => p.Name == exchangeName) ??
                        new ExchangeInfo();

                    configAction?.Invoke(exchangeInfo.Config);

                    CreateExchange(exchangeName, exchangeInfo.Config); //todo: like this
                });

        }
        #endregion

        #region Create Queue
        /// <summary>
        /// Creates the queue.
        /// </summary>
        /// <param name="queueName">Name of the queue.</param>
        /// <param name="config">The configuration.</param>
        /// <returns></returns>
        public virtual IFluentRabbit CreateQueue(string queueName, CreateQueueConfig config)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    //validation
                    config.ThrowExceptionIfNull<ArgumentNullException>(nameof(config));
                    RabbitMqClient.Model.ThrowExceptionIfNull<ModelIsNullException>();

                    //execution
                    RabbitMqClient.Model.QueueDeclare(queueName, config.Durable, config.Exclusive, config.AutoDelete, config.Arguments);
                });
        }

        /// <summary>
        /// Creates the queue.
        /// </summary>
        /// <param name="queueName">Name of the queue.</param>
        /// <param name="configAction">The configuration action.</param>
        /// <returns></returns>
        public virtual IFluentRabbit CreateQueue(string queueName, Action<CreateQueueConfig> configAction = null)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    var queueInfo = Configuration.Queues.FirstOrDefault(p => p.Name == queueName) ?? new QueueInfo();
                    configAction?.Invoke(queueInfo.Config);

                    CreateQueue(queueName, queueInfo.Config);
                });
        }
        #endregion

        #region Bind / Unbind
        /// <summary>
        /// Binds the specified exchange name.
        /// </summary>
        /// <param name="exchangeName">Name of the exchange.</param>
        /// <param name="queueName">Name of the queue.</param>
        /// <param name="routingKey">The routing key.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Bind(string exchangeName, string queueName, string routingKey, IDictionary<string, object> arguments = null)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    //validation
                    RabbitMqClient.Model.ThrowExceptionIfNull<ModelIsNullException>();

                    //execution
                    RabbitMqClient.Model.QueueBind(queueName, exchangeName, routingKey, arguments);
                });
        }

        /// <summary>
        /// Binds the specified configuration.
        /// </summary>
        /// <param name="config">The configuration.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Bind(BindingConfig config)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    //validation
                    config.ThrowExceptionIfNull<ArgumentNullException>(nameof(config));
                    config.ExchangeName.ThrowExceptionIf(string.IsNullOrWhiteSpace, $"{nameof(config.ExchangeName)} is null.");
                    config.QueueName.ThrowExceptionIf(string.IsNullOrWhiteSpace, $"{nameof(config.QueueName)} is null.");
                    config.RoutingKey.ThrowExceptionIf(string.IsNullOrWhiteSpace, $"{nameof(config.RoutingKey)} is null.");

                    //result
                    Bind(config.ExchangeName, config.QueueName, config.RoutingKey, config.Arguments);
                });
        }

        /// <summary>
        /// Binds the specified configuration action.
        /// </summary>
        /// <param name="configAction">The configuration action.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Bind(Action<BindingConfig> configAction)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    var config = new BindingConfig();
                    configAction?.Invoke(config);
                    Configuration.Bindings.Add(new BindingInfo() { Config = config });

                    Bind(config);
                });
        }

        /// <summary>
        /// Unbinds the specified exchange name.
        /// </summary>
        /// <param name="exchangeName">Name of the exchange.</param>
        /// <param name="queueName">Name of the queue.</param>
        /// <param name="routingKey">The routing key.</param>
        /// <param name="arguments">The arguments.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Unbind(string exchangeName, string queueName, string routingKey, IDictionary<string, object> arguments = null)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    //validation
                    RabbitMqClient.Model.ThrowExceptionIfNull<ModelIsNullException>();

                    //execution
                    RabbitMqClient.Model.QueueUnbind(queueName, exchangeName, routingKey, arguments);
                });
        }

        /// <summary>
        /// Unbinds the specified configuration.
        /// </summary>
        /// <param name="config">The configuration.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Unbind(BindingConfig config)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    //validation
                    config.ThrowExceptionIfNull<ArgumentNullException>(nameof(config));

                    //result
                    Unbind(config.ExchangeName, config.QueueName, config.RoutingKey, config.Arguments);
                });
        }

        /// <summary>
        /// Unbinds the specified configuration action.
        /// </summary>
        /// <param name="configAction">The configuration action.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Unbind(Action<BindingConfig> configAction)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    var config = new BindingConfig();
                    configAction?.Invoke(config);

                    Unbind(config);
                }
            );
        }

        #endregion

        #region Subscribe
        /// <summary>
        /// Subscribes the specified queue name.
        /// </summary>
        /// <param name="queueName">Name of the queue.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="config">The configuration.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Subscribe(string queueName, Action<BasicDeliverEventArgs> callback, SubscribeConfig config)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    config.ThrowExceptionIfNull<ArgumentNullException>(nameof(config));
                    RabbitMqClient.Model.ThrowExceptionIfNull<ModelIsNullException>();

                    var consumer = new EventingBasicConsumer(RabbitMqClient.Model);
                    consumer.Received += (ch, ea) =>
                    {
                        callback?.Invoke(ea);

                        if (!config.AutoAck)
                        {
                            RabbitMqClient.Model.BasicAck(ea.DeliveryTag, false);
                            FireTraceOccured(MethodBase.GetCurrentMethod(), "BasicAck called.");
                        }

                        FireTraceOccured(MethodBase.GetCurrentMethod(), "Message Received.");
                    };

                    config.ConsumerTag = RabbitMqClient.Model.BasicConsume(queueName, config.AutoAck, config.ConsumerTag, config.NoLocal, config.Exclusive, config.Arguments, consumer);
                });
        }

        /// <summary>
        /// Subscribes the specified queue name.
        /// </summary>
        /// <param name="queueName">Name of the queue.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="configAction">The configuration action.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Subscribe(string queueName, Action<BasicDeliverEventArgs> callback, Action<SubscribeConfig> configAction = null)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    var config = new SubscribeConfig();
                    configAction?.Invoke(config);

                    Subscribe(queueName, callback, config);
                });
        }


        #endregion


        /// <summary>
        /// Fetches the specified queue name.
        /// </summary>
        /// <param name="queueName">Name of the queue.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="config">The configuration.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Fetch(string queueName, Action<BasicGetResult> callback, FetchConfig config)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    //validation
                    config.ThrowExceptionIfNull<ArgumentNullException>(nameof(config));
                    RabbitMqClient.Model.ThrowExceptionIfNull<ModelIsNullException>();

                    //execution
                    var result = RabbitMqClient.Model.BasicGet(queueName, config.AutoAck);

                    callback?.Invoke(result);
                });


        }

        /// <summary>
        /// Fetches the specified queue name.
        /// </summary>
        /// <param name="queueName">Name of the queue.</param>
        /// <param name="callback">The callback.</param>
        /// <param name="configAction">The configuration action.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Fetch(string queueName, Action<BasicGetResult> callback, Action<FetchConfig> configAction = null)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    var config = new FetchConfig();
                    configAction?.Invoke(config);

                    Fetch(queueName, callback, config);
                });
        }

        /// <summary>
        /// Sleeps the specified milliseconds.
        /// </summary>
        /// <param name="frequency">The frequency.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Sleep(int frequency = 1000)
        {
            Task.Delay(frequency).GetAwaiter().GetResult();

            return this;
        }

        /// <summary>
        /// Waits the until.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="timeout">The timeout. minus values like -1 means forever.</param>
        /// <param name="frequency">The frequency. waits as milliseconds until next try.</param>
        /// <returns></returns>
        /// <exception cref="TimeoutException"></exception>
        public virtual IFluentRabbit WaitUntil(Func<bool> condition, int timeout = 1000, int frequency = 25)
        {
            return WaitUntil(condition,
                p =>
                {
                    p.Timeout = timeout;
                    p.Frequency = frequency;
                });
        }

        /// <summary>
        /// Waits the until.
        /// </summary>
        /// <param name="condition">The condition.</param>
        /// <param name="configAction">The configuration action.</param>
        /// <returns></returns>
        /// <exception cref="TimeoutException"></exception>
        public virtual IFluentRabbit WaitUntil(Func<bool> condition, Action<WaitUntilConfig> configAction)
        {
            var old = new WaitUntilConfig();
            configAction?.Invoke(old);

            void NewConfigAction(NetCore.Utilities.WaitUntilConfig p)
            {
                p.Timeout = old.Timeout;
                p.Frequency = old.Frequency;
                p.ThrowTimeoutException = old.ThrowTimeoutException;
            }

            NetCore.Utilities.TimeUtilities.WaitUntilAsync(condition, NewConfigAction).GetAwaiter().GetResult();

            return this;
        }




        #region Publish

        /// <summary>
        /// Publishes the specified data.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <param name="config">The configuration.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Publish(byte[] data, PublishConfig config)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    //validation
                    config.ThrowExceptionIfNull<ArgumentNullException>(nameof(config));
                    config.Validate();
                    RabbitMqClient.Model.ThrowExceptionIfNull<ModelIsNullException>();

                    //execution
                    RabbitMqClient.Model.BasicPublish(config.ExchangeName, config.RoutingKey, config.Mandatory, config.BasicProperties, data);
                });


        }

        /// <summary>
        /// Publishes the specified payload.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="payload">The payload.</param>
        /// <param name="config">The configuration.</param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public virtual IFluentRabbit Publish<T>(T payload, PublishConfig config)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    byte[] body = null;

                    if (config.PayloadFormat == PayloadFormat.String)
                    {
                        payload.ThrowExceptionIf(p => p.GetType() != typeof(string), "Payload type is not a string!");
                        body = Encoding.UTF8.GetBytes(payload.ToString());
                    }
                    else if (config.PayloadFormat == PayloadFormat.ByteArray)
                    {
                        if (typeof(T) == typeof(byte[]))
                        {
                            body = payload as byte[];
                        }
                        else
                        {
                            var bf = new BinaryFormatter();
                            using var ms = new MemoryStream();
                            bf.Serialize(ms, payload);

                            body = ms.ToArray();
                        }
                    }

                    Publish(body, config);
                });

        }

        /// <summary>
        /// Publishes the specified payload.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="payload">The payload.</param>
        /// <param name="configAction">The configuration action.</param>
        /// <returns></returns>
        public virtual IFluentRabbit Publish<T>(T payload, Action<PublishConfig> configAction)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    var config = new PublishConfig();
                    configAction.Invoke(config);

                    Publish(payload, config);
                });
        }

        #endregion

        #region Delete Queue
        /// <summary>
        /// Deletes the queue.
        /// </summary>
        /// <param name="queueName">Name of the queue.</param>
        /// <param name="config">The configuration.</param>
        /// <returns></returns>
        public virtual IFluentRabbit DeleteQueue(string queueName, DeleteQueueConfig config)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    //validation
                    config.ThrowExceptionIfNull<ArgumentNullException>(nameof(config));
                    RabbitMqClient.Model.ThrowExceptionIfNull<ModelIsNullException>();

                    //execution
                    RabbitMqClient.Model.QueueDelete(queueName, config.IfUnused, config.IfEmpty);
                });
        }

        /// <summary>
        /// Deletes the queue.
        /// </summary>
        /// <param name="queueName">Name of the queue.</param>
        /// <param name="configAction">The configuration action.</param>
        /// <returns></returns>
        public virtual IFluentRabbit DeleteQueue(string queueName, Action<DeleteQueueConfig> configAction = null)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    var config = new DeleteQueueConfig();
                    configAction?.Invoke(config);

                    DeleteQueue(queueName, config);
                });
        }
        #endregion

        /// <summary>
        /// Purges the queue.
        /// </summary>
        /// <param name="queueName">Name of the queue.</param>
        /// <param name="resultAction">The result action.</param>
        /// <returns></returns>
        public virtual IFluentRabbit PurgeQueue(string queueName, Action<uint> resultAction = null)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    var purgeResult = RabbitMqClient.Model.QueuePurge(queueName);
                    resultAction?.Invoke(purgeResult);
                });
        }



        #region Delete Exchange
        /// <summary>
        /// Deletes the exchange.
        /// </summary>
        /// <param name="exchangeName">Name of the exchange.</param>
        /// <param name="config">The configuration.</param>
        /// <returns></returns>
        public virtual IFluentRabbit DeleteExchange(string exchangeName, DeleteExchangeConfig config)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    //validation
                    config.ThrowExceptionIfNull<ArgumentNullException>(nameof(config));
                    RabbitMqClient.Model.ThrowExceptionIfNull<ModelIsNullException>();

                    //execution
                    RabbitMqClient.Model.ExchangeDelete(exchangeName, config.IfUnused);
                });
        }

        /// <summary>
        /// Deletes the exchange.
        /// </summary>
        /// <param name="exchangeName">Name of the exchange.</param>
        /// <param name="configAction">The configuration action.</param>
        /// <returns></returns>
        public virtual IFluentRabbit DeleteExchange(string exchangeName, Action<DeleteExchangeConfig> configAction = null)
        {
            return TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    var config = new DeleteExchangeConfig();
                    configAction?.Invoke(config);

                    DeleteExchange(exchangeName, config);
                });
        }
        #endregion


        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public virtual void Dispose()
        {
            TryCatch_Trace(MethodBase.GetCurrentMethod(),
                () =>
                {
                    RabbitMqClient.Model?.Close();
                    RabbitMqClient.Connection?.Close();

                    RabbitMqClient.Model?.Dispose();
                    RabbitMqClient.Connection?.Dispose();

                    RabbitMqClient.Model = null;
                    RabbitMqClient.Connection = null;

                });
        }
    }



}
