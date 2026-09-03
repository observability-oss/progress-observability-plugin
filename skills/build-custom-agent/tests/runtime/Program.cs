var assertions = KnowledgeBaseTests.Run() + ChatHistoryTests.Run() + await RuntimeFlowTests.RunAsync() +
                 await BehaviorCheckerTests.RunAsync();
Console.WriteLine($"RUNTIME_TESTS_OK assertions={assertions}");
