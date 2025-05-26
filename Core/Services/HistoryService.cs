// Core/Services/HistoryService.cs (SQLite 操作は後で実装)
using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using AIChatHelper.
Models;

namespace AIChatHelper.Core.Services;
public class HistoryService : IHistoryService
{
	private readonly string _connectionString;

	public HistoryService()
	{
		// 実行ファイルと同じフォルダに history.db を置く
		var exeDir = AppDomain.CurrentDomain.BaseDirectory;
		var dbPath = System.IO.Path.Combine(exeDir, "history.db");
		_connectionString = $"Data Source={dbPath}";

		EnsureDatabase();
	}

	// テーブルがなければ作成
	private void EnsureDatabase()
	{
		using var conn = new SqliteConnection(_connectionString);
		conn.Open();

		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS History (
                    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                    Text      TEXT    NOT NULL,
                    CreatedAt DATETIME NOT NULL
                );";
		cmd.ExecuteNonQuery();
	}

	public void AddHistory(string text)
	{
		using var conn = new SqliteConnection(_connectionString);
		conn.Open();

		// 直前の履歴テキストを取得して同一なら何もしない
		using (var checkCmd = conn.CreateCommand())
		{
			checkCmd.CommandText = @"
            SELECT Text
              FROM History
             ORDER BY CreatedAt DESC
             LIMIT 1;";
			var last = checkCmd.ExecuteScalar() as string;
			if (last == text)
				return;
		}

		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
                INSERT INTO History (Text, CreatedAt)
                VALUES ($text, $createdAt);";
		cmd.Parameters.AddWithValue("$text", text);
		cmd.Parameters.AddWithValue("$createdAt", DateTime.UtcNow);

		cmd.ExecuteNonQuery();
	}

	public IEnumerable<string> GetHistories()
	{
		using var conn = new SqliteConnection(_connectionString);
		conn.Open();

		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
                SELECT Text
                FROM History
                ORDER BY CreatedAt DESC;";
		using var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			yield return reader.GetString(0);
		}
	}
	public IEnumerable<HistoryItem> GetHistoryRecords()
	{
		using var conn = new SqliteConnection(_connectionString);
		conn.Open();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
                SELECT Id, Text, CreatedAt
                  FROM History
                 ORDER BY CreatedAt DESC;";
		using var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			var utc = reader.GetDateTime(2);
			var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
			yield return new HistoryItem
			{
				Id = reader.GetInt32(0),
				Text = reader.GetString(1),
				CreatedAt = local
			};
		}
	}

	public void DeleteHistory(int id)
	{
		using var conn = new SqliteConnection(_connectionString);
		conn.Open();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"DELETE FROM History WHERE Id = $id;";
		cmd.Parameters.AddWithValue("$id", id);
		cmd.ExecuteNonQuery();
	}

	public void ClearAllHistories()
	{
		using var conn = new SqliteConnection(_connectionString);
		conn.Open();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"DELETE FROM History;";
		cmd.ExecuteNonQuery();
	}
	// 最新のヒストリアイテムを1つだけ取得する最適化メソッド
	public HistoryItem? GetLatestHistoryItem()
	{
		using var conn = new SqliteConnection(_connectionString);
		conn.Open();
		using var cmd = conn.CreateCommand();
		cmd.CommandText = @"
            SELECT Id, Text, CreatedAt
              FROM History
             ORDER BY CreatedAt DESC
             LIMIT 1;";
		using var reader = cmd.ExecuteReader();
		if (reader.Read())
		{
			var utc = reader.GetDateTime(2);
			var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
			return new HistoryItem
			{
				Id = reader.GetInt32(0),
				Text = reader.GetString(1),
				CreatedAt = local
			};
		}
		return null;
	}

	// 指定した日付の履歴を取得するメソッド
	public IEnumerable<HistoryItem> GetHistoryByDate(DateTime date)
	{
		using var conn = new SqliteConnection(_connectionString);
		conn.Open();
		using var cmd = conn.CreateCommand();

		// 日付の開始と終了を設定
		var startDate = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Local);
		var endDate = startDate.AddDays(1);

		// UTC変換
		var startUtc = startDate.ToUniversalTime();
		var endUtc = endDate.ToUniversalTime();

		cmd.CommandText = @"
            SELECT Id, Text, CreatedAt
              FROM History
             WHERE CreatedAt >= $startDate AND CreatedAt < $endDate
             ORDER BY CreatedAt ASC;";
		cmd.Parameters.AddWithValue("$startDate", startUtc);
		cmd.Parameters.AddWithValue("$endDate", endUtc);

		using var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			var utc = reader.GetDateTime(2);
			var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
			yield return new HistoryItem
			{
				Id = reader.GetInt32(0),
				Text = reader.GetString(1),
				CreatedAt = local
			};
		}
	}

	// 利用可能な日付の一覧を取得
	public IEnumerable<DateTime> GetAvailableDates()
	{
		using var conn = new SqliteConnection(_connectionString);
		conn.Open();
		using var cmd = conn.CreateCommand();

		// 日付ごとにグループ化して取得
		cmd.CommandText = @"
            SELECT DISTINCT date(CreatedAt) as DateOnly
              FROM History
             ORDER BY DateOnly ASC;";

		using var reader = cmd.ExecuteReader();
		while (reader.Read())
		{
			string dateStr = reader.GetString(0);
			if (DateTime.TryParse(dateStr, out DateTime date))
			{
				yield return date.Date;
			}
		}
	}
}
