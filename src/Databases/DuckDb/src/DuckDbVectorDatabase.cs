﻿using LangChain.Databases.Connectors;
using Microsoft.SemanticKernel.Connectors.DuckDB;

namespace LangChain.Databases.DuckDb
{
    public class DuckDbVectorDatabase(DuckDBMemoryStore store) : SemanticKernelMemoryDatabase(store);
}
