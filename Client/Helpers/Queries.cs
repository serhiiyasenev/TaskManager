using Client.Models;
using System.Timers;

namespace Client.Helpers;

public class Queries(List<Func<Task>> tasks, HttpClient? httpClient)
{
    public async void ExecuteRandomTask(object sender, ElapsedEventArgs e)
    {
        try
        {
            var randomIndex = new Random().Next(tasks.Count);

            // Execute task via it's invoke here
            var taskId = tasks[randomIndex]().Id;

            var model = new ExecutedTaskDto { TaskId = taskId, TaskName = tasks[randomIndex].Method.Name };

            var addedTask = await AddExecutedTaskAsync(model);
        }
        catch (Exception ex)
        {
            await Console.Out.WriteLineAsync($"ExecuteRandomTask error occurred: {ex.Message}");
        }
    }

    private async Task<ExecutedTaskDto?> AddExecutedTaskAsync(ExecutedTaskDto executedTaskDto)
    {
        var responseMessage = await httpClient!.SendAsync(new HttpRequestMessage(HttpMethod.Post, "Tasks/AddExecutedTask").AddContent(executedTaskDto));
        var addedTask = await responseMessage.GetModelAsync<ExecutedTaskDto>();
        return addedTask;
    }
}