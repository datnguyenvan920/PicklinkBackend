using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Shared;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public List<string> Errors { get; set; } = new();

    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string error) =>
        new() { Success = false, Errors = { error } };

    public static ApiResponse<T> Fail(List<string> errors) =>
        new() { Success = false, Errors = errors };
}
