using System.ComponentModel.DataAnnotations;
using Aiursoft.ChessServer.Models;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.ChessServer.Models.ViewModels;

public class RoomViewModel : UiStackLayoutViewModel
{
    public RoomViewModel() {
        PageTitle = "Chess room";
    }
    public int Id { get; init;
    }
    public bool IsCreator { get; init;
    }
    public string Message { get; init;
    } = "";
}
