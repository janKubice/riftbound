using UnityEngine;

public interface ITooltipData
{
    string GetHeader();
    string GetItemType();
    string GetDescription();
    Color GetColor(); 
}
