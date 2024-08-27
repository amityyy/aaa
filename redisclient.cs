public async Task<List<T?>> ListRangeAsync<T>(string key, long start, long stop)
{
    IDatabase database = await GetDatabaseAsync().ConfigureAwait(false);
    RedisValue[] values = await database.ListRangeAsync(key, start, stop).ConfigureAwait(false);
    List<T?> result = new List<T?>();

    foreach (RedisValue value in values)
    {
        if (!value.IsNull && !string.IsNullOrEmpty(value))
        {
            T? item = JsonSerializer.Deserialize<T?>(value.ToString());
            result.Add(item);
        }
    }

    return result;
}
